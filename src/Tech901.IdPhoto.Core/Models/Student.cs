namespace Tech901.IdPhoto.Core.Models;

public record Student(
    string StudentId,
    string FirstName,
    string LastName,
    string? PreferredName,
    Dictionary<string, string> ExtraFields);
