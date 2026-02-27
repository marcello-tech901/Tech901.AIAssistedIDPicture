using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Configuration;

namespace Tech901.IdPhoto.Infrastructure.Speech;

/// <summary>
/// Provides text-to-speech (TTS) and speech-to-text (STT) capabilities using the
/// Azure Cognitive Services Speech SDK.
/// </summary>
/// <remarks>
/// <para>
/// AI-102: This service demonstrates the Azure Speech SDK pattern — authenticating via
/// <see cref="SpeechConfig.FromSubscription"/>, selecting a neural voice for synthesis,
/// and handling <see cref="ResultReason"/> outcomes for both TTS and STT operations.
/// </para>
/// <para>
/// Key production patterns: the <see cref="SpeechConfig"/> is created once in the constructor
/// and reused across calls (it is thread-safe), while <see cref="AudioConfig"/> and
/// <see cref="SpeechSynthesizer"/>/<see cref="SpeechRecognizer"/> are created per-call
/// because they hold device handles that must be released promptly.
/// </para>
/// </remarks>
public sealed class AzureSpeechService : ISpeechService
{
    private readonly AzureSpeechOptions _options;

    // AI-102: SpeechConfig is cached in the constructor because creating it validates the
    // subscription key and region. Reusing it avoids repeated validation overhead and
    // is the recommended pattern from the Speech SDK documentation.
    private readonly SpeechConfig _speechConfig;
    private readonly ILogger<AzureSpeechService> _logger;

    // Pre-warmed recognizer fields — see PrepareListenAsync for explanation
    private AudioConfig? _preparedAudioConfig;
    private SpeechRecognizer? _preparedRecognizer;

    /// <summary>
    /// Gets whether the Speech service has valid credentials configured.
    /// </summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.Key) && !string.IsNullOrWhiteSpace(_options.Region);

    /// <summary>
    /// Initializes the Azure Speech SDK with subscription credentials and voice settings.
    /// </summary>
    /// <param name="options">Speech service key, region, voice name, and device IDs.</param>
    /// <param name="logger">Logger for synthesis/recognition diagnostics.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="AzureSpeechOptions.Key"/> or <see cref="AzureSpeechOptions.Region"/>
    /// is missing. The DI factory in <c>ServiceCollectionExtensions</c> prevents this by
    /// checking before construction, but the guard protects against direct instantiation.
    /// </exception>
    public AzureSpeechService(IOptions<AzureSpeechOptions> options, ILogger<AzureSpeechService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Key) || string.IsNullOrWhiteSpace(_options.Region))
            throw new InvalidOperationException("Azure Speech Key and Region must be configured");

        // AI-102: SpeechConfig.FromSubscription authenticates using a subscription key + region pair.
        // The region determines which Azure data center handles requests (e.g., "eastus").
        _speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);

        // AI-102: Neural voices (e.g., "en-US-JennyNeural") produce more natural-sounding speech
        // than standard voices. They are the recommended choice for production applications.
        _speechConfig.SpeechSynthesisVoiceName = _options.Voice;
        _speechConfig.SpeechRecognitionLanguage = "en-US";
        _logger.LogInformation("Azure Speech SDK initialized (Voice={Voice})", _options.Voice);
    }

    /// <summary>
    /// Synthesizes the given text to speech and plays it through the configured speaker device.
    /// </summary>
    /// <param name="text">The text to speak aloud.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <remarks>
    /// AI-102: AudioConfig controls the output device. <c>FromSpeakerOutput(deviceId)</c>
    /// targets a specific speaker (useful for kiosks with dedicated audio hardware), while
    /// <c>FromDefaultSpeakerOutput()</c> uses the system default.
    /// </remarks>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Speech service is not configured; skipping TTS");
            return;
        }

        try
        {
            // AI-102: AudioConfig targets a specific speaker device when configured,
            // falling back to the system default. Each synthesis call gets a fresh config
            // because the audio device handle should not be held open between calls.
            using var audioConfig = !string.IsNullOrWhiteSpace(_options.SpeakerDeviceId)
                ? AudioConfig.FromSpeakerOutput(_options.SpeakerDeviceId)
                : AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer = new SpeechSynthesizer(_speechConfig, audioConfig);

            // AI-102: SpeakTextAsync sends plain text to the Speech service, which returns
            // audio streamed to the configured output device. For SSML control (pitch, rate,
            // emphasis), use SpeakSsmlAsync instead.
            using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

            // AI-102: ResultReason.Canceled indicates a service-side failure (auth error,
            // quota exceeded, network issue). Always check cancellation details for diagnostics.
            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError("Speech synthesis canceled: {Reason} - {Details}", cancellation.Reason, cancellation.ErrorDetails);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech synthesis failed for text of length {Length}", text.Length);
        }
    }

    /// <summary>
    /// Pre-warms a <see cref="SpeechRecognizer"/> so the next <see cref="ListenAsync"/> call
    /// starts faster by avoiding device-open latency.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The pre-warming pattern creates the recognizer and audio config ahead of time. Opening
    /// a microphone device can take 200-500ms, so doing it before the user is ready to speak
    /// provides a smoother kiosk experience. The pre-warmed recognizer is consumed (and disposed)
    /// by the next <see cref="ListenAsync"/> call.
    /// </remarks>
    public Task PrepareListenAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
            return Task.CompletedTask;

        DisposePrepared();

        _preparedAudioConfig = !string.IsNullOrWhiteSpace(_options.MicrophoneDeviceId)
            ? AudioConfig.FromMicrophoneInput(_options.MicrophoneDeviceId)
            : AudioConfig.FromDefaultMicrophoneInput();
        _preparedRecognizer = new SpeechRecognizer(_speechConfig, _preparedAudioConfig);

        _logger.LogDebug("Pre-warmed SpeechRecognizer for upcoming listen");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Listens for a single spoken utterance and returns the recognized text.
    /// </summary>
    /// <param name="timeout">
    /// Maximum time to wait for speech. If elapsed, recognition is stopped and <c>null</c> is returned.
    /// </param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>The recognized text, or <c>null</c> if no speech was detected or an error occurred.</returns>
    /// <remarks>
    /// AI-102: <c>RecognizeOnceAsync</c> performs single-shot recognition — it listens for one
    /// utterance and returns. For continuous dictation, use <c>StartContinuousRecognitionAsync</c>.
    /// The timeout pattern uses <c>Task.WhenAny</c> to race the recognition against a delay,
    /// ensuring the kiosk does not hang indefinitely if no one speaks.
    /// </remarks>
    public async Task<string?> ListenAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Speech service is not configured; skipping STT");
            return null;
        }

        try
        {
            AudioConfig? freshAudioConfig = null;
            SpeechRecognizer recognizer;

            // Use pre-warmed recognizer if available, otherwise create a fresh one
            if (_preparedRecognizer is not null)
            {
                recognizer = _preparedRecognizer;
                _preparedRecognizer = null;
                _logger.LogDebug("Using pre-warmed SpeechRecognizer");
            }
            else
            {
                freshAudioConfig = !string.IsNullOrWhiteSpace(_options.MicrophoneDeviceId)
                    ? AudioConfig.FromMicrophoneInput(_options.MicrophoneDeviceId)
                    : AudioConfig.FromDefaultMicrophoneInput();
                recognizer = new SpeechRecognizer(_speechConfig, freshAudioConfig);
            }

            try
            {
                var recognizeTask = recognizer.RecognizeOnceAsync();

                // AI-102: Task.WhenAny races the recognition against a timeout delay.
                // This prevents the kiosk from blocking indefinitely if no one speaks.
                if (timeout.HasValue)
                {
                    var completed = await Task.WhenAny(recognizeTask, Task.Delay(timeout.Value, ct)).ConfigureAwait(false);
                    if (completed != recognizeTask)
                    {
                        _logger.LogInformation("Speech recognition timed out");
                        await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);

                        // Observed task pattern: attach a continuation so any fault is logged
                        // rather than becoming an unobserved TaskException
                        _ = recognizeTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                        return null;
                    }
                }

                var result = await recognizeTask.ConfigureAwait(false);

                // AI-102: ResultReason tells us the outcome of the recognition attempt.
                // RecognizedSpeech = success, NoMatch = audio heard but not understood,
                // other reasons indicate service-level issues.
                return result.Reason switch
                {
                    ResultReason.RecognizedSpeech => result.Text,
                    ResultReason.NoMatch =>
                        Do(() => _logger.LogInformation("No speech recognized")),
                    _ =>
                        Do(() => _logger.LogWarning("Speech recognition failed: {Reason}", result.Reason))
                };
            }
            finally
            {
                recognizer.Dispose();
                freshAudioConfig?.Dispose();
                DisposePreparedAudioConfig();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech recognition failed");
            return null;
        }
    }

    /// <summary>
    /// Disposes both the pre-warmed recognizer and its audio config.
    /// </summary>
    private void DisposePrepared()
    {
        _preparedRecognizer?.Dispose();
        _preparedRecognizer = null;
        DisposePreparedAudioConfig();
    }

    /// <summary>
    /// Disposes only the pre-warmed audio config (the recognizer may have been consumed separately).
    /// </summary>
    private void DisposePreparedAudioConfig()
    {
        _preparedAudioConfig?.Dispose();
        _preparedAudioConfig = null;
    }

    /// <summary>
    /// Helper that executes a side-effect action and returns <c>null</c>, enabling
    /// logging within switch expressions.
    /// </summary>
    private static string? Do(Action action) { action(); return null; }
}
