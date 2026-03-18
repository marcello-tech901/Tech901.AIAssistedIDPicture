using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Configuration;
using CoreVoiceInfo = Tech901.IdPhoto.Core.Models.VoiceInfo;

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

        // TODO DEMO-13: AI-102 — SpeechConfig.FromSubscription(key, region). Config is created once and cached (thread-safe). Set breakpoint here to inspect _speechConfig fields.

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
            // TODO DEMO-14: AI-102 — TTS: Fresh AudioConfig per call (device handles must be released). SpeakTextAsync for plain text, SpeakSsmlAsync for pitch/rate control. Set breakpoint on line 111 to inspect SpeechSynthesisResult.

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

    // TODO DEMO-21: AI-102 — SSML synthesis. Compare SpeakTextAsync (plain text) vs SpeakSsmlAsync (SSML markup). Set breakpoint to inspect the SSML document and the SpeechSynthesisResult.

    /// <inheritdoc />
    /// <remarks>
    /// AI-102: SpeakSsmlAsync sends an SSML document to the Speech service. The SSML must
    /// be a complete document with a &lt;speak&gt; root element. The voice specified in SSML
    /// overrides the SpeechConfig voice, allowing per-utterance voice switching.
    /// </remarks>
    public async Task SpeakSsmlAsync(string ssml, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Speech service is not configured; skipping SSML TTS");
            return;
        }

        try
        {
            using var audioConfig = !string.IsNullOrWhiteSpace(_options.SpeakerDeviceId)
                ? AudioConfig.FromSpeakerOutput(_options.SpeakerDeviceId)
                : AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer = new SpeechSynthesizer(_speechConfig, audioConfig);

            // AI-102: SpeakSsmlAsync accepts a full SSML document. Unlike SpeakTextAsync,
            // you control voice, prosody, breaks, emphasis, and phonemes via XML elements.
            using var result = await synthesizer.SpeakSsmlAsync(ssml).ConfigureAwait(false);

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError("SSML synthesis canceled: {Reason} - {Details}", cancellation.Reason, cancellation.ErrorDetails);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSML synthesis failed");
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
 

    // TODO DEMO-16: AI-102 — Pre-warming pattern. Opening a microphone takes 200-500ms. Creating the recognizer early eliminates perceived latency for the user.
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
                // TODO DEMO-15: AI-102 — STT: RecognizeOnceAsync = single-shot recognition. Task.WhenAny races it against timeout. Set breakpoint on line 219 to inspect result.Reason and result.Text.
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

    /// <inheritdoc />
    public string CurrentVoice => _speechConfig.SpeechSynthesisVoiceName;

    /// <inheritdoc />
    public void SetVoice(string voiceShortName)
    {
        _speechConfig.SpeechSynthesisVoiceName = voiceShortName;
        _logger.LogInformation("TTS voice changed to {Voice}", voiceShortName);
    }

    // TODO DEMO-17: AI-102 — Voice enumeration. GetVoicesAsync("en-US") lists all available neural voices. Set breakpoint on line 286 to browse the voice catalog.

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoreVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default)
    {
        try
        {
            // AI-102: SpeechSynthesizer.GetVoicesAsync retrieves the list of available voices
            // from the Speech service. Using AudioConfig = null avoids opening a speaker device.
            using var synthesizer = new SpeechSynthesizer(_speechConfig, null as AudioConfig);
            var result = await synthesizer.GetVoicesAsync("en-US").ConfigureAwait(false);

            if (result.Reason == ResultReason.VoicesListRetrieved && result.Voices.Count > 0)
            {
                var voices = result.Voices
                    .Where(v => v.Locale.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                    .Select(v => new CoreVoiceInfo(v.ShortName, v.LocalName, v.Gender.ToString()))
                    .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _logger.LogInformation("Retrieved {Count} en-US voices from Azure", voices.Count);
                return voices;
            }

            _logger.LogWarning("Azure voice list retrieval returned {Reason}, using fallback list", result.Reason);
            return FallbackVoices;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve voices from Azure, using fallback list");
            return FallbackVoices;
        }
    }

    /// <summary>
    /// Static fallback list of popular en-US neural voices returned when the Azure fetch fails.
    /// </summary>
    internal static readonly IReadOnlyList<CoreVoiceInfo> FallbackVoices =
    [
        new("en-US-AIGenerate1Neural", "AI Generate 1", "Male"),
        new("en-US-AIGenerate2Neural", "AI Generate 2", "Female"),
        new("en-US-AmberNeural", "Amber", "Female"),
        new("en-US-AnaNeural", "Ana", "Female"),
        new("en-US-AriaNeural", "Aria", "Female"),
        new("en-US-AshleyNeural", "Ashley", "Female"),
        new("en-US-BrandonNeural", "Brandon", "Male"),
        new("en-US-ChristopherNeural", "Christopher", "Male"),
        new("en-US-CoraNeural", "Cora", "Female"),
        new("en-US-DavisNeural", "Davis", "Male"),
        new("en-US-ElizabethNeural", "Elizabeth", "Female"),
        new("en-US-EricNeural", "Eric", "Male"),
        new("en-US-GuyNeural", "Guy", "Male"),
        new("en-US-JacobNeural", "Jacob", "Male"),
        new("en-US-JaneNeural", "Jane", "Female"),
        new("en-US-JasonNeural", "Jason", "Male"),
        new("en-US-JennyNeural", "Jenny", "Female"),
        new("en-US-MichelleNeural", "Michelle", "Female"),
        new("en-US-MonicaNeural", "Monica", "Female"),
        new("en-US-NancyNeural", "Nancy", "Female"),
        new("en-US-SaraNeural", "Sara", "Female"),
        new("en-US-TonyNeural", "Tony", "Male"),
    ];

    /// <summary>
    /// Helper that executes a side-effect action and returns <c>null</c>, enabling
    /// logging within switch expressions.
    /// </summary>
    private static string? Do(Action action) { action(); return null; }
}
