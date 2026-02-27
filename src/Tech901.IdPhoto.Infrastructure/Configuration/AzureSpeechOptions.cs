namespace Tech901.IdPhoto.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for the Azure Cognitive Services Speech SDK,
/// bound from the <c>Azure:Speech</c> section of application configuration.
/// </summary>
/// <remarks>
/// <para>
/// AI-102: This class demonstrates the <strong>Options Pattern</strong> (<c>IOptions&lt;T&gt;</c>),
/// the recommended way to consume configuration in .NET. Instead of reading raw strings from
/// <c>IConfiguration</c>, services receive a validated, strongly-typed object.
/// </para>
/// <para>
/// Configuration layering (last wins): <c>appsettings.json</c> → User Secrets → Environment Variables.
/// On the kiosk, credentials are provided via environment variables (<c>Azure__Speech__Key</c>,
/// <c>Azure__Speech__Region</c>) using the double-underscore (<c>__</c>) separator convention.
/// </para>
/// </remarks>
public class AzureSpeechOptions
{
    /// <summary>
    /// The configuration section path that maps to this options class.
    /// </summary>
    /// <remarks>
    /// AI-102: By convention, the <c>SectionName</c> constant keeps the binding path
    /// co-located with the options class, so DI registration code references
    /// <c>AzureSpeechOptions.SectionName</c> rather than a magic string.
    /// </remarks>
    public const string SectionName = "Azure:Speech";

    /// <summary>
    /// The Azure Speech subscription key used for authentication.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The Azure region where the Speech resource is deployed (e.g., "eastus").
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// The neural voice name for text-to-speech synthesis (e.g., "en-US-JennyNeural").
    /// </summary>
    /// <remarks>
    /// AI-102: Neural voices use deep neural networks for more natural prosody and
    /// pronunciation. The default <c>en-US-JennyNeural</c> is a general-purpose US English voice.
    /// This can be overridden by the <c>Kiosk:SpeechVoice</c> configuration key.
    /// </remarks>
    public string Voice { get; set; } = "en-US-JennyNeural";

    /// <summary>
    /// Optional device ID for a specific microphone input. When <c>null</c>, the system
    /// default microphone is used.
    /// </summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>
    /// Optional device ID for a specific speaker output. When <c>null</c>, the system
    /// default speaker is used.
    /// </summary>
    public string? SpeakerDeviceId { get; set; }
}
