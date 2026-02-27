using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using FuzzySharp;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Services;

/// <summary>
/// Manages the cohort roster for a photo session, including CSV import, fuzzy name matching,
/// and session-state persistence. Thread-safe: all mutable state is guarded by <c>_lock</c>
/// so the service can be shared across UI and background threads without races.
/// </summary>
public class RosterService : IRosterService
{
    private readonly ILogger<RosterService> _logger;

    // Single lock guards all mutable collections (_students, _completedIds, counters).
    // This coarse-grained strategy is safe because critical sections are short (in-memory
    // list/set operations) and contention is low in a single-kiosk scenario.
    private readonly object _lock = new();
    private readonly List<Student> _students = [];
    private readonly HashSet<string> _completedIds = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _sessionStartedAt;
    private int _retakeCount;
    private int _failureCount;
    private readonly List<SessionFailure> _failures = [];

    /// <summary>
    /// Fuzzy match score at or above which a match is considered <see cref="MatchConfidence.High"/>.
    /// FuzzySharp scores range 0-100; 85 was tuned empirically to avoid false positives on
    /// common first names while still tolerating minor typos and speech-to-text errors.
    /// </summary>
    private const int HighConfidenceThreshold = 85;

    /// <summary>
    /// Minimum score to surface a match at all (<see cref="MatchConfidence.Medium"/>).
    /// Below 65, matches are almost always wrong and would confuse the participant.
    /// </summary>
    private const int MediumConfidenceThreshold = 65;

    /// <summary>
    /// Maps common CSV column header variations to canonical field names.
    /// This lets cohort coordinators export rosters from different systems (SIS, Excel, Google Sheets)
    /// without needing to rename columns to an exact schema. Case-insensitive comparison handles
    /// mixed-case headers automatically.
    /// </summary>
    private static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["StudentId"] = "StudentId",
        ["Student ID"] = "StudentId",
        ["student_id"] = "StudentId",
        ["ID"] = "StudentId",
        ["FirstName"] = "FirstName",
        ["First Name"] = "FirstName",
        ["first_name"] = "FirstName",
        ["LastName"] = "LastName",
        ["Last Name"] = "LastName",
        ["last_name"] = "LastName",
        ["PreferredName"] = "PreferredName",
        ["Preferred Name"] = "PreferredName",
        ["preferred_name"] = "PreferredName",
        ["Nickname"] = "PreferredName",
        ["Goes By"] = "PreferredName",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="RosterService"/> class.
    /// </summary>
    /// <param name="logger">Logger for roster operations and diagnostics.</param>
    public RosterService(ILogger<RosterService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses a CSV file into the in-memory roster, replacing any previously loaded data.
    /// Performs per-row validation (missing fields, duplicates, blank rows) and returns a
    /// detailed <see cref="RosterImportResult"/> so the admin UI can display import diagnostics.
    /// </summary>
    /// <param name="csvPath">Absolute or relative path to the CSV roster file.</param>
    /// <param name="ct">Cancellation token checked between rows for responsive cancellation.</param>
    /// <returns>Import statistics including counts and row-level errors/warnings.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required columns are missing from the CSV header.</exception>
    public async Task<RosterImportResult> LoadRosterAsync(string csvPath, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var reader = new StreamReader(csvPath, detectEncodingFromByteOrderMarks: true);

            // CsvHelper configuration is deliberately lenient: MissingFieldFound, HeaderValidated,
            // and BadDataFound are all suppressed so we can surface user-friendly errors via
            // RosterImportResult instead of throwing opaque CsvHelper exceptions.
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
            });

            await csv.ReadAsync().ConfigureAwait(false);
            csv.ReadHeader();

            var rawHeaders = csv.HeaderRecord ?? throw new InvalidOperationException("CSV file has no headers.");
            var headerMap = BuildHeaderMap(rawHeaders);

            // Fail fast if the CSV is missing StudentId, FirstName, or LastName columns.
            ValidateRequiredColumns(headerMap);

            var errors = new List<RosterImportRowError>();
            var warnings = new List<RosterImportRowWarning>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownFields = new HashSet<string> { "StudentId", "FirstName", "LastName", "PreferredName" };
            int rowNumber = 1; // 1-based, after header
            int imported = 0;
            int skipped = 0;
            int duplicates = 0;
            var parsedStudents = new List<Student>();

            while (await csv.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;

                try
                {
                    var studentId = GetMappedField(csv, headerMap, "StudentId");
                    var firstName = GetMappedField(csv, headerMap, "FirstName");
                    var lastName = GetMappedField(csv, headerMap, "LastName");

                    // Blank row detection: all required fields empty
                    if (string.IsNullOrWhiteSpace(studentId) &&
                        string.IsNullOrWhiteSpace(firstName) &&
                        string.IsNullOrWhiteSpace(lastName))
                    {
                        warnings.Add(new RosterImportRowWarning(rowNumber, "Row", "Blank row skipped"));
                        skipped++;
                        continue;
                    }

                    // Per-field validation
                    var hasError = false;
                    if (string.IsNullOrWhiteSpace(studentId))
                    {
                        errors.Add(new RosterImportRowError(rowNumber, "StudentId is required"));
                        hasError = true;
                    }
                    if (string.IsNullOrWhiteSpace(firstName))
                    {
                        errors.Add(new RosterImportRowError(rowNumber, "FirstName is required"));
                        hasError = true;
                    }
                    if (string.IsNullOrWhiteSpace(lastName))
                    {
                        errors.Add(new RosterImportRowError(rowNumber, "LastName is required"));
                        hasError = true;
                    }

                    if (hasError)
                    {
                        skipped++;
                        continue;
                    }

                    var trimmedId = studentId!.Trim();
                    var trimmedFirst = firstName!.Trim();
                    var trimmedLast = lastName!.Trim();

                    // Duplicate detection — HashSet.Add returns false if the ID already exists
                    if (!seenIds.Add(trimmedId))
                    {
                        warnings.Add(new RosterImportRowWarning(rowNumber, "StudentId",
                            $"Duplicate StudentId '{trimmedId}' skipped"));
                        duplicates++;
                        skipped++;
                        continue;
                    }

                    var preferredName = GetMappedOptionalField(csv, headerMap, "PreferredName");

                    // Capture any extra columns (e.g., "Cohort", "Track") so they can be used
                    // in filename templates like "{Track}_{LastName}.jpg".
                    var extraFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rawHeader in rawHeaders)
                    {
                        // Skip columns we've already mapped to known fields
                        if (headerMap.TryGetValue(rawHeader, out var mapped) && knownFields.Contains(mapped))
                            continue;

                        var value = csv.GetField(rawHeader);
                        if (!string.IsNullOrWhiteSpace(value))
                            extraFields[rawHeader] = value.Trim();
                    }

                    parsedStudents.Add(new Student(trimmedId, trimmedFirst, trimmedLast, preferredName, extraFields));
                    imported++;
                }
                catch (Exception ex)
                {
                    errors.Add(new RosterImportRowError(rowNumber, $"Parse error: {ex.Message}"));
                    skipped++;
                }
            }

            // Atomically swap the roster under the lock so concurrent FindMatches calls
            // never see a partially-loaded list.
            lock (_lock)
            {
                _students.Clear();
                _completedIds.Clear();
                _sessionStartedAt = DateTime.UtcNow;
                _students.AddRange(parsedStudents);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Loaded {Imported} students from roster {Path} ({Skipped} skipped, {Duplicates} duplicates)",
                imported, csvPath, skipped, duplicates);

            return new RosterImportResult
            {
                ImportedCount = imported,
                SkippedCount = skipped,
                TotalRowsProcessed = rowNumber - 1, // subtract header row
                DuplicateCount = duplicates,
                Duration = stopwatch.Elapsed,
                Errors = errors,
                Warnings = warnings,
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roster from {Path}", csvPath);
            throw;
        }
    }

    /// <summary>
    /// Performs fuzzy name matching against the roster using FuzzySharp's token-set ratio algorithm.
    /// Token-set ratio is preferred over simple ratio because it is order-insensitive: "John Smith"
    /// and "Smith, John" score equally. Participants who have already completed their photo are
    /// excluded so they don't appear as options again.
    /// </summary>
    /// <param name="name">The spoken or typed name to match (may include first, last, or preferred name).</param>
    /// <returns>Up to 5 matches at or above <see cref="MediumConfidenceThreshold"/>, sorted by score descending.</returns>
    public IReadOnlyList<RosterMatch> FindMatches(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];

        var normalizedInput = name.Trim().ToLowerInvariant();

        lock (_lock)
        {
            var matches = _students
                .Where(s => !_completedIds.Contains(s.StudentId))
                .Select(s =>
                {
                    // Score against the participant's legal name
                    var fullName = $"{s.FirstName} {s.LastName}".ToLowerInvariant();
                    var score = Fuzz.TokenSetRatio(normalizedInput, fullName);

                    // Also score against preferred/nickname if present, and keep whichever is higher.
                    // This handles cases like a participant named "Robert" who goes by "Bob".
                    if (s.PreferredName is not null)
                    {
                        var altName = $"{s.PreferredName} {s.LastName}".ToLowerInvariant();
                        var altScore = Fuzz.TokenSetRatio(normalizedInput, altName);
                        score = Math.Max(score, altScore);
                    }

                    var confidence = score >= HighConfidenceThreshold ? MatchConfidence.High
                        : score >= MediumConfidenceThreshold ? MatchConfidence.Medium
                        : MatchConfidence.Low;

                    return new RosterMatch(s, score, confidence);
                })
                .Where(m => m.Score >= MediumConfidenceThreshold)
                .OrderByDescending(m => m.Score)
                .Take(5)
                .ToList();

            _logger.LogInformation("Found {Count} matches for name '{Name}'", matches.Count, name);
            return matches;
        }
    }

    /// <summary>
    /// Records that a participant's photo has been captured and finalized.
    /// Once marked, the participant will no longer appear in <see cref="FindMatches"/> results.
    /// </summary>
    /// <param name="studentId">The unique identifier of the participant.</param>
    public void MarkCompleted(string studentId)
    {
        lock (_lock)
        {
            _completedIds.Add(studentId);
        }
        _logger.LogInformation("Marked student {StudentId} as completed", studentId);
    }

    /// <summary>
    /// Checks whether a participant has already completed their photo session.
    /// </summary>
    /// <param name="studentId">The unique identifier of the participant.</param>
    /// <returns><c>true</c> if the participant's photo has been finalized; otherwise <c>false</c>.</returns>
    public bool IsCompleted(string studentId)
    {
        lock (_lock)
        {
            return _completedIds.Contains(studentId);
        }
    }

    /// <summary>
    /// Returns a snapshot of all participants in the currently loaded roster.
    /// The returned list is a copy, so callers cannot mutate internal state.
    /// </summary>
    public IReadOnlyList<Student> GetAllStudents()
    {
        lock (_lock)
        {
            return _students.ToList();
        }
    }

    /// <summary>
    /// Returns participants whose photos have already been captured this session.
    /// </summary>
    public IReadOnlyList<Student> GetCompletedStudents()
    {
        lock (_lock)
        {
            return _students.Where(s => _completedIds.Contains(s.StudentId)).ToList();
        }
    }

    /// <summary>
    /// Returns participants who still need their photo taken this session.
    /// </summary>
    public IReadOnlyList<Student> GetRemainingStudents()
    {
        lock (_lock)
        {
            return _students.Where(s => !_completedIds.Contains(s.StudentId)).ToList();
        }
    }

    /// <summary>
    /// Captures a point-in-time snapshot of the current session (completed IDs, counters, failures).
    /// Used by the admin panel to display progress and by <see cref="SaveSessionStateAsync"/> for persistence.
    /// </summary>
    public SessionState GetSessionState()
    {
        lock (_lock)
        {
            return new SessionState
            {
                CompletedStudentIds = new HashSet<string>(_completedIds),
                TotalStudents = _students.Count,
                StartedAt = _sessionStartedAt,
                RetakeCount = _retakeCount,
                FailureCount = _failureCount,
                Failures = new List<SessionFailure>(_failures),
            };
        }
    }

    /// <summary>
    /// Persists the current session state to a JSON file so a session can survive application restarts.
    /// Errors are logged but not thrown, because losing session state is recoverable (the admin
    /// can re-scan participants) while crashing the kiosk is not.
    /// </summary>
    /// <param name="path">File path to write the JSON session state.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveSessionStateAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var state = GetSessionState();
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
            _logger.LogInformation("Session state saved to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session state to {Path}", path);
        }
    }

    /// <summary>
    /// Restores session state from a previously saved JSON file. If the file does not exist,
    /// this is a no-op (first run of the day). Errors are logged but swallowed to avoid
    /// blocking kiosk startup over a corrupted state file.
    /// </summary>
    /// <param name="path">File path to read the JSON session state from.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadSessionStateAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<SessionState>(json);
            if (state is null) return;

            lock (_lock)
            {
                _completedIds.Clear();
                foreach (var id in state.CompletedStudentIds)
                    _completedIds.Add(id);

                _sessionStartedAt = state.StartedAt;
                _retakeCount = state.RetakeCount;
                _failureCount = state.FailureCount;
                _failures.Clear();
                _failures.AddRange(state.Failures);
            }

            _logger.LogInformation("Session state loaded from {Path} ({Completed}/{Total} completed)",
                path, _completedIds.Count, _students.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session state from {Path}", path);
        }
    }

    /// <summary>
    /// Maps raw CSV headers to canonical field names using <see cref="ColumnAliases"/>.
    /// Unrecognized headers are ignored (they become extra fields on the Student model).
    /// </summary>
    private static Dictionary<string, string> BuildHeaderMap(string[] headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (ColumnAliases.TryGetValue(header, out var canonical))
                map[header] = canonical;
        }
        return map;
    }

    /// <summary>
    /// Validates that all required columns (StudentId, FirstName, LastName) resolved from the header map.
    /// Throws early with a clear message listing which columns are missing.
    /// </summary>
    private static void ValidateRequiredColumns(Dictionary<string, string> headerMap)
    {
        var required = new[] { "StudentId", "FirstName", "LastName" };
        var resolved = new HashSet<string>(headerMap.Values, StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(r => !resolved.Contains(r)).ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"CSV is missing required columns: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Retrieves a CSV field value using the header map to translate from canonical name to raw header.
    /// Returns <c>null</c> if the canonical field has no mapped column in this CSV.
    /// </summary>
    private static string? GetMappedField(CsvReader csv, Dictionary<string, string> headerMap, string canonicalName)
    {
        var rawHeader = headerMap.FirstOrDefault(kv => kv.Value == canonicalName).Key;
        if (rawHeader is null) return null;
        return csv.GetField(rawHeader);
    }

    /// <summary>
    /// Retrieves an optional CSV field, trimming whitespace and converting empty strings to <c>null</c>.
    /// </summary>
    private static string? GetMappedOptionalField(CsvReader csv, Dictionary<string, string> headerMap, string canonicalName)
    {
        var rawHeader = headerMap.FirstOrDefault(kv => kv.Value == canonicalName).Key;
        if (rawHeader is null) return null;
        var value = csv.GetField(rawHeader);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
