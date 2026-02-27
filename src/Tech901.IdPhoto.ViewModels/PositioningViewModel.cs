using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class PositioningViewModel : ObservableObject, IDisposable
{
    private readonly IFaceDetectionService _faceDetection;
    private readonly ISpeechService _speech;
    private readonly ICameraService _camera;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<PositioningViewModel> _logger;
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _voiceCts;
    private string? _lastSpokenGuidance;

    private static readonly string[] TriggerPhrases =
        ["take photo", "take picture", "capture", "cheese", "snap", "take it", "ready"];

    [ObservableProperty]
    private RectangleF? _faceRectangle;

    [ObservableProperty]
    private string _guidanceMessage = "Center your face in the frame";

    [ObservableProperty]
    private bool _readyToCapture;

    [ObservableProperty]
    private byte[]? _currentFrame;

    [ObservableProperty]
    private bool _hasFaceDetection;

    [ObservableProperty]
    private double _faceRectX;

    [ObservableProperty]
    private double _faceRectY;

    [ObservableProperty]
    private double _faceRectWidth;

    [ObservableProperty]
    private double _faceRectHeight;

    public event Action? CaptureRequested;

    public PositioningViewModel(
        IFaceDetectionService faceDetection,
        ISpeechService speech,
        ICameraService camera,
        IDispatcher dispatcher,
        ILogger<PositioningViewModel> logger)
    {
        _faceDetection = faceDetection;
        _speech = speech;
        _camera = camera;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task StartGuidanceAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        // Subscribe to camera frames for live preview
        _camera.FrameCaptured += OnFrameCaptured;

        _logger.LogInformation("Started positioning guidance");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500, ct);

                if (!_camera.IsRunning)
                {
                    _dispatcher.Invoke(() =>
                    {
                        GuidanceMessage = "Camera not available. Press Take Photo when ready.";
                        ReadyToCapture = true;
                    });
                    continue;
                }

                byte[] frame;
                try
                {
                    frame = await _camera.CaptureFrameAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to capture frame during positioning");
                    continue;
                }

                var result = await _faceDetection.DetectFaceAsync(frame, ct);

                if (result is null)
                {
                    _dispatcher.Invoke(() =>
                    {
                        FaceRectangle = null;
                        HasFaceDetection = false;
                        GuidanceMessage = "Look straight at the camera";
                        ReadyToCapture = true;
                    });
                    SpeakGuidanceIfChanged("Look straight at the camera");
                    continue;
                }

                _dispatcher.Invoke(() =>
                {
                    FaceRectangle = result.FaceRectangle;
                    HasFaceDetection = true;
                    FaceRectX = result.FaceRectangle.X;
                    FaceRectY = result.FaceRectangle.Y;
                    FaceRectWidth = result.FaceRectangle.Width;
                    FaceRectHeight = result.FaceRectangle.Height;
                    UpdateGuidance(result);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during positioning guidance");
        }
    }

    private void OnFrameCaptured(object? sender, byte[] frame)
    {
        _dispatcher.Invoke(() => CurrentFrame = frame);
    }

    private void UpdateGuidance(FaceDetectionResult face)
    {
        var rect = face.FaceRectangle;

        // Normalize pixel coordinates to 0-1 range using actual camera frame size
        var frameW = (float)_camera.FrameWidth;
        var frameH = (float)_camera.FrameHeight;
        if (frameW <= 0 || frameH <= 0)
        {
            GuidanceMessage = "Camera not ready";
            ReadyToCapture = false;
            return;
        }

        var normW = rect.Width / frameW;
        var normH = rect.Height / frameH;
        var centerX = (rect.X + rect.Width / 2) / frameW;
        var centerY = (rect.Y + rect.Height / 2) / frameH;

        const float tolerance = 0.15f;
        const float minSize = 0.2f;
        const float maxSize = 0.6f;

        string message;
        if (normW < minSize || normH < minSize)
        {
            message = "Move closer to the camera";
            ReadyToCapture = false;
        }
        else if (normW > maxSize || normH > maxSize)
        {
            message = "Move back a little";
            ReadyToCapture = false;
        }
        else if (Math.Abs(centerX - 0.5f) > tolerance)
        {
            // Preview is mirrored: physical left appears on screen-right.
            // When centerX < 0.5 the face is physically to the right,
            // so tell user to move left (toward their physical left in the mirror).
            message = centerX < 0.5f ? "Move slightly to the left" : "Move slightly to the right";
            ReadyToCapture = false;
        }
        else if (Math.Abs(centerY - 0.5f) > tolerance)
        {
            message = centerY < 0.5f ? "Move down slightly" : "Move up slightly";
            ReadyToCapture = false;
        }
        else
        {
            message = _speech.IsAvailable
                ? "Perfect! Hold still. Say \"Cheese\" or press the button."
                : "Perfect! Hold still.";
            ReadyToCapture = true;
        }

        GuidanceMessage = message;
        SpeakGuidanceIfChanged(message);
    }

    private void SpeakGuidanceIfChanged(string message)
    {
        if (message == _lastSpokenGuidance)
            return;

        _lastSpokenGuidance = message;
        FireAndForget(_speech.SpeakAsync(message));
    }

    [RelayCommand(CanExecute = nameof(ReadyToCapture))]
    private void Capture()
    {
        _logger.LogInformation("Capture requested from positioning");
        CaptureRequested?.Invoke();
    }

    partial void OnReadyToCaptureChanged(bool value)
    {
        CaptureCommand.NotifyCanExecuteChanged();

        if (value)
            StartVoiceListening();
        else
            StopVoiceListening();
    }

    private void StartVoiceListening()
    {
        if (!_speech.IsAvailable)
            return;

        StopVoiceListening();
        _voiceCts = new CancellationTokenSource();
        FireAndForget(VoiceListenLoopAsync(_voiceCts.Token));
    }

    private void StopVoiceListening()
    {
        _voiceCts?.Cancel();
        _voiceCts?.Dispose();
        _voiceCts = null;
    }

    private async Task VoiceListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Voice capture listening started");
        try
        {
            while (!ct.IsCancellationRequested && ReadyToCapture)
            {
                var text = await _speech.ListenAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested || !ReadyToCapture)
                    break;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var lower = text.ToLowerInvariant();
                if (Array.Exists(TriggerPhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice trigger detected: \"{Text}\"", text);
                    _dispatcher.Invoke(Capture);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during voice listening");
        }

        _logger.LogInformation("Voice capture listening stopped");
    }

    public void StopGuidance()
    {
        _camera.FrameCaptured -= OnFrameCaptured;
        StopVoiceListening();
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public void Dispose()
    {
        StopGuidance();
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
