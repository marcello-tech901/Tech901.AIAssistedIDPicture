using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.ViewModels;

public partial class ReviewViewModel : ObservableObject, IDisposable
{
    private readonly ISpeechService _speech;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<ReviewViewModel> _logger;
    private CancellationTokenSource? _voiceCts;

    private static readonly string[] AcceptPhrases =
        ["accept", "use it", "looks good", "use this", "yes", "keep it", "looks great"];

    private static readonly string[] RetakePhrases =
        ["retake", "try again", "redo", "no", "take another"];

    [ObservableProperty]
    private byte[] _capturedImage = [];

    [ObservableProperty]
    private bool _isListening;

    public event Action<byte[]>? Accepted;
    public event Action? RetakeRequested;

    public ReviewViewModel(
        ISpeechService speech,
        IDispatcher dispatcher,
        ILogger<ReviewViewModel> logger)
    {
        _speech = speech;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void LoadImage(byte[] imageData)
    {
        CapturedImage = imageData;
    }

    public void StartVoiceListening()
    {
        if (!_speech.IsAvailable)
            return;

        _voiceCts?.Cancel();
        _voiceCts?.Dispose();
        _voiceCts = new CancellationTokenSource();
        _dispatcher.Invoke(() => IsListening = true);
        FireAndForget(VoiceListenLoopAsync(_voiceCts.Token));
    }

    private async Task VoiceListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Review voice listening started");
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

                if (Array.Exists(AcceptPhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice accept detected: \"{Text}\"", text);
                    _dispatcher.Invoke(Accept);
                    break;
                }

                if (Array.Exists(RetakePhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice retake detected: \"{Text}\"", text);
                    _dispatcher.Invoke(Retake);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during review voice listening");
        }
        finally
        {
            _dispatcher.Invoke(() => IsListening = false);
        }

        _logger.LogInformation("Review voice listening stopped");
    }

    [RelayCommand]
    private void Accept()
    {
        _logger.LogInformation("Photo accepted");
        Accepted?.Invoke(CapturedImage);
    }

    [RelayCommand]
    private void Retake()
    {
        _logger.LogInformation("Retake requested");
        RetakeRequested?.Invoke();
    }

    public void Dispose()
    {
        try
        {
            _voiceCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        _voiceCts?.Dispose();
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
