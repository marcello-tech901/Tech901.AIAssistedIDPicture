namespace Tech901.IdPhoto.Core.Models;

public record PersistedRoster
{
    public string? SourceFilePath { get; init; }
    public DateTime ImportedAt { get; init; }
    public List<Student> Students { get; init; } = [];
}
