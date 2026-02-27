using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

public interface IRosterService
{
    Task<RosterImportResult> LoadRosterAsync(string csvPath, CancellationToken ct = default);
    IReadOnlyList<RosterMatch> FindMatches(string name);
    void MarkCompleted(string studentId);
    bool IsCompleted(string studentId);
    IReadOnlyList<Student> GetAllStudents();
    IReadOnlyList<Student> GetCompletedStudents();
    IReadOnlyList<Student> GetRemainingStudents();
    SessionState GetSessionState();
    Task SaveSessionStateAsync(string path, CancellationToken ct = default);
    Task LoadSessionStateAsync(string path, CancellationToken ct = default);
}
