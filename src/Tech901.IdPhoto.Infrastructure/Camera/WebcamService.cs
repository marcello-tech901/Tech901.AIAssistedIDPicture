using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Configuration;

namespace Tech901.IdPhoto.Infrastructure.Camera;

public sealed class WebcamService : ICameraService
{
    private readonly ILogger<WebcamService> _logger;
    private readonly CameraOptions _options;
    private readonly object _stateLock = new();
    private readonly object _frameLock = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _captureLoop;
    private byte[]? _currentFrame;
    private int _frameWidth;
    private int _frameHeight;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _capture is not null && _capture.IsOpened() && _cts is not null && !_cts.IsCancellationRequested;
            }
        }
    }

    public int FrameWidth
    {
        get { lock (_stateLock) { return _frameWidth; } }
    }

    public int FrameHeight
    {
        get { lock (_stateLock) { return _frameHeight; } }
    }

    public event EventHandler<byte[]>? FrameCaptured;

    public WebcamService(ILogger<WebcamService> logger, IOptions<CameraOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public Task StartAsync(int deviceIndex = 0, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_capture is not null && _capture.IsOpened() && _cts is not null && !_cts.IsCancellationRequested)
                return Task.CompletedTask;

            _capture = new VideoCapture(deviceIndex);
            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                _capture = null;
                throw new InvalidOperationException($"Could not open camera device {deviceIndex}.");
            }

            _capture.Set(VideoCaptureProperties.FrameWidth, _options.Width);
            _capture.Set(VideoCaptureProperties.FrameHeight, _options.Height);

            var actualWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
            var actualHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);
            _frameWidth = actualWidth;
            _frameHeight = actualHeight;
            _logger.LogInformation(
                "Camera started on device {DeviceIndex} — requested {ReqW}x{ReqH}, actual {ActW}x{ActH}",
                deviceIndex, _options.Width, _options.Height, actualWidth, actualHeight);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _captureLoop = Task.Run(() => CaptureLoopAsync(_cts.Token), _cts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        VideoCapture? capture;

        lock (_stateLock)
        {
            cts = _cts;
            loop = _captureLoop;
            capture = _capture;
            _cts = null;
            _captureLoop = null;
            _capture = null;
            _frameWidth = 0;
            _frameHeight = 0;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            if (loop is not null)
            {
                try { await loop; }
                catch (OperationCanceledException) { }
            }

            cts.Dispose();
        }

        capture?.Release();
        capture?.Dispose();

        _logger.LogInformation("Camera stopped");
    }

    public Task<byte[]> CaptureFrameAsync()
    {
        lock (_stateLock)
        {
            if (_capture is null || !_capture.IsOpened())
                throw new InvalidOperationException("Camera is not running.");
        }

        lock (_frameLock)
        {
            if (_currentFrame is null)
                throw new InvalidOperationException("No frame available yet.");
            return Task.FromResult((byte[])_currentFrame.Clone());
        }
    }

    public IReadOnlyList<string> EnumerateDevices()
    {
        var devices = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            using var cap = new VideoCapture(i);
            if (cap.IsOpened())
            {
                devices.Add($"Camera {i}");
                cap.Release();
            }
            else
            {
                break;
            }
        }

        return devices;
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        using var mat = new Mat();
        var consecutiveFailures = 0;
        const int maxConsecutiveFailures = 20;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                VideoCapture? capture;
                lock (_stateLock)
                {
                    capture = _capture;
                }

                if (capture is null || !capture.IsOpened())
                    break;

                capture.Read(mat);
                if (!mat.Empty())
                {
                    consecutiveFailures = 0;
                    var encoded = mat.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, _options.JpegQuality));
                    lock (_frameLock)
                    {
                        _currentFrame = encoded;
                    }

                    FrameCaptured?.Invoke(this, encoded);
                    await Task.Delay(33, ct).ConfigureAwait(false); // ~30 FPS
                }
                else
                {
                    consecutiveFailures++;
                    if (consecutiveFailures == 3)
                        _logger.LogWarning("Camera producing empty frames, backing off capture loop");
                    if (consecutiveFailures >= maxConsecutiveFailures)
                    {
                        _logger.LogError("Camera exceeded {Max} consecutive failures, stopping capture loop", maxConsecutiveFailures);
                        break;
                    }
                    var backoff = Math.Min(5000, 100 * (1 << Math.Min(consecutiveFailures, 6)));
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                if (consecutiveFailures <= 3)
                    _logger.LogError(ex, "Error in capture loop");
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    _logger.LogError("Camera exceeded {Max} consecutive failures, stopping capture loop", maxConsecutiveFailures);
                    break;
                }
                var backoff = Math.Min(5000, 100 * (1 << Math.Min(consecutiveFailures, 6)));
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Task? loop;
        VideoCapture? capture;

        lock (_stateLock)
        {
            cts = _cts;
            loop = _captureLoop;
            capture = _capture;
            _cts = null;
            _captureLoop = null;
            _capture = null;
            _frameWidth = 0;
            _frameHeight = 0;
        }

        cts?.Cancel();
        try { loop?.Wait(TimeSpan.FromMilliseconds(500)); }
        catch
        {
            _logger.LogWarning("Capture loop did not complete within 500ms during Dispose");
        }

        cts?.Dispose();
        capture?.Release();
        capture?.Dispose();
    }
}
