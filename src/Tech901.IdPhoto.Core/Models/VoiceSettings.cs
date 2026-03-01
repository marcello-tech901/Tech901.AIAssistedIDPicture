namespace Tech901.IdPhoto.Core.Models;

/// <summary>
/// Persisted voice preference, serialized to <c>voice-settings.json</c> in the output directory.
/// </summary>
public class VoiceSettings
{
    public string SelectedVoice { get; set; } = string.Empty;
}
