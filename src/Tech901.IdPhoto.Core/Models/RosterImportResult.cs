namespace Tech901.IdPhoto.Core.Models;

public record RosterImportResult
{
    public int ImportedCount { get; init; }
    public int SkippedCount { get; init; }
    public int TotalRowsProcessed { get; init; }
    public int DuplicateCount { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<RosterImportRowError> Errors { get; init; } = [];
    public IReadOnlyList<RosterImportRowWarning> Warnings { get; init; } = [];
    public bool IsSuccess => Errors.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;
}

public record RosterImportRowError(int RowNumber, string Message);

public record RosterImportRowWarning(int RowNumber, string Field, string Message);
