namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Defines text-to-speech (TTS) and speech-to-text (STT) capabilities used
/// by the kiosk to guide participants through the ID photo workflow.
/// </summary>
/// <remarks>
/// Backed by Azure Cognitive Services Speech SDK (AI-102). When Azure credentials
/// are not configured, the DI container provides a <c>NullSpeechService</c> that
/// reports <see cref="IsAvailable"/> as <see langword="false"/>, enabling the
/// application to degrade gracefully to text-only interaction.
/// </remarks>
public interface ISpeechService
{
    /// <summary>
    /// Gets a value indicating whether the speech service is operational.
    /// </summary>
    /// <remarks>
    /// ViewModels check this property to decide whether to show visual-only
    /// prompts or to supplement them with spoken audio. Always returns
    /// <see langword="false"/> in the null fallback implementation.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// Synthesizes the specified text as speech and plays it through the default audio device.
    /// </summary>
    /// <param name="text">The text to speak aloud.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A task that completes when playback finishes or is cancelled.</returns>
    Task SpeakAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Listens for speech input from the default microphone and returns the recognized text.
    /// </summary>
    /// <param name="timeout">
    /// Maximum duration to wait for speech. When <see langword="null"/>, a
    /// service-defined default timeout is used.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// The recognized text, or <see langword="null"/> if no speech was detected
    /// within the timeout period.
    /// </returns>
    Task<string?> ListenAsync(TimeSpan? timeout = null, CancellationToken ct = default);

    /// <summary>
    /// Pre-warms the speech recognizer so that subsequent calls to
    /// <see cref="ListenAsync"/> start faster.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A task that completes when the recognizer is ready.</returns>
    /// <remarks>
    /// Call this during a UI transition so the microphone and recognition
    /// pipeline are initialized before the participant begins speaking.
    /// </remarks>
    Task PrepareListenAsync(CancellationToken ct = default);
}
