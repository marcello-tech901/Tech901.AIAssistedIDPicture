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

public class RosterService : IRosterService
{
    private readonly ILogger<RosterService> _logger;
    private readonly object _lock = new();
    private readonly List<Student> _students = [];
    private readonly HashSet<string> _completedIds = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _sessionStartedAt;
    private int _retakeCount;
    private int _failureCount;
    private readonly List<SessionFailure> _failures = [];

    private const int HighConfidenceThreshold = 85;
    private const int MediumConfidenceThreshold = 65;

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

    public RosterService(ILogger<RosterService> logger)
    {
        _logger = logger;
    }

    public async Task<RosterImportResult> LoadRosterAsync(string csvPath, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var reader = new StreamReader(csvPath, detectEncodingFromByteOrderMarks: true);
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

                    // Duplicate detection
                    if (!seenIds.Add(trimmedId))
                    {
                        warnings.Add(new RosterImportRowWarning(rowNumber, "StudentId",
                            $"Duplicate StudentId '{trimmedId}' skipped"));
                        duplicates++;
                        skipped++;
                        continue;
                    }

                    var preferredName = GetMappedOptionalField(csv, headerMap, "PreferredName");

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
                    var fullName = $"{s.FirstName} {s.LastName}".ToLowerInvariant();
                    var score = Fuzz.TokenSetRatio(normalizedInput, fullName);

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

    public void MarkCompleted(string studentId)
    {
        lock (_lock)
        {
            _completedIds.Add(studentId);
        }
        _logger.LogInformation("Marked student {StudentId} as completed", studentId);
    }

    public bool IsCompleted(string studentId)
    {
        lock (_lock)
        {
            return _completedIds.Contains(studentId);
        }
    }

    public IReadOnlyList<Student> GetAllStudents()
    {
        lock (_lock)
        {
            return _students.ToList();
        }
    }

    public IReadOnlyList<Student> GetCompletedStudents()
    {
        lock (_lock)
        {
            return _students.Where(s => _completedIds.Contains(s.StudentId)).ToList();
        }
    }

    public IReadOnlyList<Student> GetRemainingStudents()
    {
        lock (_lock)
        {
            return _students.Where(s => !_completedIds.Contains(s.StudentId)).ToList();
        }
    }

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

    private static void ValidateRequiredColumns(Dictionary<string, string> headerMap)
    {
        var required = new[] { "StudentId", "FirstName", "LastName" };
        var resolved = new HashSet<string>(headerMap.Values, StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(r => !resolved.Contains(r)).ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"CSV is missing required columns: {string.Join(", ", missing)}");
    }

    private static string? GetMappedField(CsvReader csv, Dictionary<string, string> headerMap, string canonicalName)
    {
        var rawHeader = headerMap.FirstOrDefault(kv => kv.Value == canonicalName).Key;
        if (rawHeader is null) return null;
        return csv.GetField(rawHeader);
    }

    private static string? GetMappedOptionalField(CsvReader csv, Dictionary<string, string> headerMap, string canonicalName)
    {
        var rawHeader = headerMap.FirstOrDefault(kv => kv.Value == canonicalName).Key;
        if (rawHeader is null) return null;
        var value = csv.GetField(rawHeader);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
