namespace Tech901.IdPhoto.Infrastructure.Configuration;

public class AzureSpeechOptions
{
    public const string SectionName = "Azure:Speech";

    public string Key { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Voice { get; set; } = "en-US-JennyNeural";
    public string? MicrophoneDeviceId { get; set; }
    public string? SpeakerDeviceId { get; set; }
}
