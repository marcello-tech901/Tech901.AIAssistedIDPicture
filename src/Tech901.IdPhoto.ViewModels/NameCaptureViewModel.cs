using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.ViewModels;

public partial class NameCaptureViewModel : ObservableObject, IDisposable
{
    private readonly ISpeechService _speech;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<NameCaptureViewModel> _logger;
    private CancellationTokenSource? _listenCts;

    [ObservableProperty]
    private string? _recognizedText;

    [ObservableProperty]
    private string? _typedName;

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private SpeechState _speechState = SpeechState.Unavailable;

    [ObservableProperty]
    private string? _statusText;

    public bool IsSpeechAvailable => _speech.IsAvailable;

    public bool HasRecognitionResult => SpeechState == SpeechState.Recognized;
    public bool RecognitionFailed => SpeechState == SpeechState.Failed;

    public event Action<string>? NameSubmitted;

    public NameCaptureViewModel(
        ISpeechService speech,
        IDispatcher dispatcher,
        ILogger<NameCaptureViewModel> logger)
    {
        _speech = speech;
        _dispatcher = dispatcher;
        _logger = logger;

        SpeechState = _speech.IsAvailable ? SpeechState.Ready : SpeechState.Unavailable;
    }

    partial void OnSpeechStateChanged(SpeechState value)
    {
        StatusText = value switch
        {
            SpeechState.Ready => "Get ready to say your name...",
            SpeechState.Listening => "Listening... say your name",
            SpeechState.Recognized => $"I heard: {RecognizedText}",
            SpeechState.Failed => "I didn't catch that",
            _ => null
        };

        OnPropertyChanged(nameof(HasRecognitionResult));
        OnPropertyChanged(nameof(RecognitionFailed));
    }

    /// <summary>
    /// Called by KioskFlowViewModel after TTS welcome prompt finishes.
    /// </summary>
    public async Task StartAsync()
    {
        if (!_speech.IsAvailable)
        {
            _dispatcher.Invoke(() => SpeechState = SpeechState.Unavailable);
            return;
        }

        _dispatcher.Invoke(() => SpeechState = SpeechState.Ready);
        await ListenInternalAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ListenAsync(CancellationToken ct)
    {
        if (!_speech.IsAvailable)
            return;

        await ListenInternalAsync(ct).ConfigureAwait(false);
    }

    private async Task ListenInternalAsync(CancellationToken externalCt = default)
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _listenCts.Token;

        _dispatcher.Invoke(() =>
        {
            IsListening = true;
            SpeechState = SpeechState.Listening;
        });

        try
        {
            _logger.LogInformation("Starting speech recognition");
            var result = await _speech.ListenAsync(TimeSpan.FromSeconds(10), ct);

            if (!string.IsNullOrWhiteSpace(result))
            {
                _dispatcher.Invoke(() =>
                {
                    RecognizedText = result;
                    SpeechState = SpeechState.Recognized;
                });
                _logger.LogInformation("Speech recognized: {Text}", result);
            }
            else
            {
                _dispatcher.Invoke(() =>
                {
                    RecognizedText = null;
                    SpeechState = SpeechState.Failed;
                });
                _logger.LogWarning("No speech recognized");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech recognition failed");
            _dispatcher.Invoke(() => SpeechState = SpeechState.Failed);
        }
        finally
        {
            _dispatcher.Invoke(() => IsListening = false);
        }
    }

    [RelayCommand]
    private void Retry()
    {
        RecognizedText = null;
        TypedName = null;
        SpeechState = _speech.IsAvailable ? SpeechState.Ready : SpeechState.Unavailable;
    }

    [RelayCommand]
    private void Submit()
    {
        var name = !string.IsNullOrWhiteSpace(RecognizedText)
            ? RecognizedText
            : TypedName;

        if (string.IsNullOrWhiteSpace(name))
            return;

        _logger.LogInformation("Name submitted: {Name}", name);
        NameSubmitted?.Invoke(name.Trim());
    }

    public void Dispose()
    {
        try
        {
            _listenCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        _listenCts?.Dispose();
    }
}
