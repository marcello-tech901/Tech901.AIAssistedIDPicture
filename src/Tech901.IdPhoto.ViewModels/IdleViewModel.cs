using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.ViewModels;

public partial class IdleViewModel : ObservableObject, IDisposable
{
    private readonly IPresenceDetector _presenceDetector;
    private readonly ICameraService _camera;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<IdleViewModel> _logger;
    private CancellationTokenSource? _pollCts;

    [ObservableProperty]
    private string _statusMessage = "Step closer to begin";

    public event Action? FaceDetected;

    public IdleViewModel(
        IPresenceDetector presenceDetector,
        ICameraService camera,
        IDispatcher dispatcher,
        ILogger<IdleViewModel> logger)
    {
        _presenceDetector = presenceDetector;
        _camera = camera;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task StartPollingAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        _logger.LogInformation("Started presence detection polling");

        var consecutiveFailures = 0;
        const int maxFailuresBeforeBackoff = 3;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var delay = consecutiveFailures >= maxFailuresBeforeBackoff
                    ? Math.Min(10_000, 3000 * (1 << (consecutiveFailures - maxFailuresBeforeBackoff)))
                    : 3000;

                await Task.Delay(delay, ct);

                if (!_camera.IsRunning)
                    continue;

                byte[] frame;
                try
                {
                    frame = await _camera.CaptureFrameAsync();
                    consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures <= maxFailuresBeforeBackoff)
                        _logger.LogWarning(ex, "Failed to capture frame for polling (attempt {Count})", consecutiveFailures);
                    else if (consecutiveFailures == maxFailuresBeforeBackoff + 1)
                        _logger.LogWarning("Camera repeatedly unavailable, backing off polling interval");
                    continue;
                }

                var present = _presenceDetector.DetectPresence(frame);

                if (present)
                {
                    _logger.LogInformation("Face detected, transitioning from Idle");
                    _dispatcher.Invoke(() => StatusMessage = "Face detected!");
                    FaceDetected?.Invoke();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during presence detection polling");
        }
    }

    public void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public void Dispose()
    {
        StopPolling();
    }
}
