namespace Tech901.IdPhoto.Core.Models;

public record SessionState
{
    public HashSet<string> CompletedStudentIds { get; init; } = [];
    public int TotalStudents { get; init; }
    public DateTime StartedAt { get; init; }
    public int RetakeCount { get; init; }
    public int FailureCount { get; init; }
    public List<SessionFailure> Failures { get; init; } = [];
}

public record SessionFailure(
    string StudentId,
    string Reason,
    DateTime OccurredAt);
