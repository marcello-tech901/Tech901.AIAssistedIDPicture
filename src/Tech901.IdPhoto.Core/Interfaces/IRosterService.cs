using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Manages the cohort roster, including CSV import, fuzzy name matching,
/// participant completion tracking, and session persistence.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe. The default <c>RosterService</c> uses
/// internal locking so that concurrent UI and background operations can safely
/// read and mutate roster state.
/// </remarks>
public interface IRosterService
{
    /// <summary>
    /// Imports participant records from a CSV file, replacing any previously loaded roster.
    /// </summary>
    /// <param name="csvPath">Absolute path to the CSV roster file.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="RosterImportResult"/> summarizing how many records were loaded
    /// and any rows that could not be parsed.
    /// </returns>
    Task<RosterImportResult> LoadRosterAsync(string csvPath, CancellationToken ct = default);

    /// <summary>
    /// Searches the loaded roster for participants whose names match the input,
    /// using fuzzy string matching to handle speech-recognition variations.
    /// </summary>
    /// <param name="name">The name (or partial name) to search for.</param>
    /// <returns>
    /// An ordered list of <see cref="RosterMatch"/> results ranked by confidence.
    /// Returns an empty list when no reasonable matches are found.
    /// </returns>
    IReadOnlyList<RosterMatch> FindMatches(string name);

    /// <summary>
    /// Marks the specified participant as having completed their ID photo.
    /// </summary>
    /// <param name="studentId">The unique identifier of the participant.</param>
    void MarkCompleted(string studentId);

    /// <summary>
    /// Determines whether the specified participant has already completed their ID photo.
    /// </summary>
    /// <param name="studentId">The unique identifier of the participant.</param>
    /// <returns><see langword="true"/> if the participant is marked complete; otherwise <see langword="false"/>.</returns>
    bool IsCompleted(string studentId);

    /// <summary>
    /// Returns all participants currently loaded in the roster.
    /// </summary>
    /// <returns>A read-only list of every <see cref="Student"/> in the roster.</returns>
    IReadOnlyList<Student> GetAllStudents();

    /// <summary>
    /// Returns only participants who have completed their ID photo.
    /// </summary>
    /// <returns>A read-only list of completed <see cref="Student"/> records.</returns>
    IReadOnlyList<Student> GetCompletedStudents();

    /// <summary>
    /// Returns only participants who have not yet completed their ID photo.
    /// </summary>
    /// <returns>A read-only list of remaining <see cref="Student"/> records.</returns>
    IReadOnlyList<Student> GetRemainingStudents();

    /// <summary>
    /// Captures a snapshot of the current session, including roster data
    /// and completion status for all participants.
    /// </summary>
    /// <returns>A <see cref="SessionState"/> representing the current session.</returns>
    SessionState GetSessionState();

    /// <summary>
    /// Persists the current session state to disk so the session can be resumed later.
    /// </summary>
    /// <param name="path">Absolute file path for the saved session.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task SaveSessionStateAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Restores a previously saved session state from disk, replacing the current
    /// roster and completion data.
    /// </summary>
    /// <param name="path">Absolute file path of the saved session to load.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task LoadSessionStateAsync(string path, CancellationToken ct = default);
}
