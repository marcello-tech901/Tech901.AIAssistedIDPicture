using System.Runtime.CompilerServices;
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
    private CancellationTokenSource? _confirmCts;

    private static readonly string[] SubmitPhrases = ["yes", "correct", "that's right", "continue"];
    private static readonly string[] RetryPhrases = ["no", "wrong", "try again", "redo"];

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

        // Cancel any ongoing confirmation loop
        CancelConfirmation();

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

                // Start voice confirmation loop
                FireAndForget(StartConfirmationAsync(result));
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

    private async Task StartConfirmationAsync(string recognizedName)
    {
        if (!_speech.IsAvailable)
            return;

        var prepareTask = _speech.PrepareListenAsync();
        await _speech.SpeakAsync($"I heard {recognizedName}. Is that correct?").ConfigureAwait(false);
        await prepareTask.ConfigureAwait(false);

        CancelConfirmation();
        _confirmCts = new CancellationTokenSource();
        await ConfirmationListenLoopAsync(_confirmCts.Token).ConfigureAwait(false);
    }

    private async Task ConfirmationListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Name confirmation voice listening started");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var text = await _speech.ListenAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                    break;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var lower = text.ToLowerInvariant();

                if (Array.Exists(SubmitPhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice confirmation accepted: \"{Text}\"", text);
                    _dispatcher.Invoke(Submit);
                    break;
                }

                if (Array.Exists(RetryPhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice confirmation denied: \"{Text}\"", text);
                    _dispatcher.Invoke(() =>
                    {
                        Retry();
                        FireAndForget(ListenInternalAsync());
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during name confirmation voice listening");
        }

        _logger.LogInformation("Name confirmation voice listening stopped");
    }

    private void CancelConfirmation()
    {
        try
        {
            _confirmCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        _confirmCts?.Dispose();
        _confirmCts = null;
    }

    [RelayCommand]
    private void Retry()
    {
        CancelConfirmation();
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
        CancelConfirmation();
        try
        {
            _listenCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        _listenCts?.Dispose();
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
