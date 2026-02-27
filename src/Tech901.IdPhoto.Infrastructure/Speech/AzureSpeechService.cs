using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Configuration;

namespace Tech901.IdPhoto.Infrastructure.Speech;

public sealed class AzureSpeechService : ISpeechService
{
    private readonly AzureSpeechOptions _options;
    private readonly SpeechConfig _speechConfig;
    private readonly ILogger<AzureSpeechService> _logger;
    private AudioConfig? _preparedAudioConfig;
    private SpeechRecognizer? _preparedRecognizer;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.Key) && !string.IsNullOrWhiteSpace(_options.Region);

    public AzureSpeechService(IOptions<AzureSpeechOptions> options, ILogger<AzureSpeechService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Key) || string.IsNullOrWhiteSpace(_options.Region))
            throw new InvalidOperationException("Azure Speech Key and Region must be configured");

        _speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);
        _speechConfig.SpeechSynthesisVoiceName = _options.Voice;
        _speechConfig.SpeechRecognitionLanguage = "en-US";
        _logger.LogInformation("Azure Speech SDK initialized (Voice={Voice})", _options.Voice);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            _logger.LogWarning("Speech service is not configured; skipping TTS");
            return;
        }

        try
        {
            using var audioConfig = !string.IsNullOrWhiteSpace(_options.SpeakerDeviceId)
                ? AudioConfig.FromSpeakerOutput(_options.SpeakerDeviceId)
                : AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer = new SpeechSynthesizer(_speechConfig, audioConfig);
            using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

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

                if (timeout.HasValue)
                {
                    var completed = await Task.WhenAny(recognizeTask, Task.Delay(timeout.Value, ct)).ConfigureAwait(false);
                    if (completed != recognizeTask)
                    {
                        _logger.LogInformation("Speech recognition timed out");
                        await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                        _ = recognizeTask.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
                        return null;
                    }
                }

                var result = await recognizeTask.ConfigureAwait(false);

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

    private void DisposePrepared()
    {
        _preparedRecognizer?.Dispose();
        _preparedRecognizer = null;
        DisposePreparedAudioConfig();
    }

    private void DisposePreparedAudioConfig()
    {
        _preparedAudioConfig?.Dispose();
        _preparedAudioConfig = null;
    }

    private static string? Do(Action action) { action(); return null; }
}
