using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.ViewModels;

public partial class CaptureViewModel : ObservableObject, IDisposable
{
    private readonly ICameraService _camera;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<CaptureViewModel> _logger;
    private CancellationTokenSource? _countdownCts;

    [ObservableProperty]
    private int _countdownValue = 3;

    [ObservableProperty]
    private byte[]? _currentFrame;

    public event Action<byte[]>? PhotoCaptured;

    public CaptureViewModel(
        ICameraService camera,
        IDispatcher dispatcher,
        ILogger<CaptureViewModel> logger)
    {
        _camera = camera;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task RunCountdownAsync(CancellationToken ct = default)
    {
        _countdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _countdownCts.Token;

        _camera.FrameCaptured += OnFrameCaptured;
        _logger.LogInformation("Starting capture countdown");

        try
        {
            for (int i = 3; i >= 1; i--)
            {
                _dispatcher.Invoke(() => CountdownValue = i);
                await Task.Delay(1000, token);
            }

            _dispatcher.Invoke(() => CountdownValue = 0);

            var frame = await _camera.CaptureFrameAsync();
            _logger.LogInformation("Photo captured ({Bytes} bytes)", frame.Length);
            PhotoCaptured?.Invoke(frame);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture photo");
        }
        finally
        {
            _camera.FrameCaptured -= OnFrameCaptured;
        }
    }

    private void OnFrameCaptured(object? sender, byte[] frame)
    {
        _dispatcher.Invoke(() => CurrentFrame = frame);
    }

    public void Dispose()
    {
        _camera.FrameCaptured -= OnFrameCaptured;
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _countdownCts = null;
    }
}
