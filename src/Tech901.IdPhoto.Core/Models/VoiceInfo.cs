namespace Tech901.IdPhoto.Core.Models;

/// <summary>
/// Metadata about an available text-to-speech voice.
/// </summary>
/// <param name="ShortName">Azure SDK voice short name (e.g., "en-US-JennyNeural").</param>
/// <param name="DisplayName">Human-readable display name (e.g., "Jenny").</param>
/// <param name="Gender">Voice gender ("Female" or "Male").</param>
public record VoiceInfo(string ShortName, string DisplayName, string Gender);
