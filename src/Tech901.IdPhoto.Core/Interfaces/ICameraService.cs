namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Abstracts webcam hardware access so the application can capture video frames
/// and still images without coupling to a specific camera library.
/// </summary>
/// <remarks>
/// Implements <see cref="IDisposable"/> because the underlying camera driver
/// (e.g., OpenCvSharp4) holds native resources that must be released explicitly.
/// Frame delivery is push-based via the <see cref="FrameCaptured"/> event.
/// </remarks>
public interface ICameraService : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the camera is currently streaming frames.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the width, in pixels, of frames produced by the active camera device.
    /// </summary>
    int FrameWidth { get; }

    /// <summary>
    /// Gets the height, in pixels, of frames produced by the active camera device.
    /// </summary>
    int FrameHeight { get; }

    /// <summary>
    /// Raised each time a new frame is captured from the camera device.
    /// The event argument contains the raw image bytes (JPEG).
    /// </summary>
    /// <remarks>
    /// Subscribers should process frames quickly or offload work to avoid
    /// blocking the capture loop.
    /// </remarks>
    event EventHandler<byte[]>? FrameCaptured;

    /// <summary>
    /// Opens the specified camera device and begins streaming frames.
    /// </summary>
    /// <param name="deviceIndex">Zero-based index of the camera device to open.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task StartAsync(int deviceIndex = 0, CancellationToken ct = default);

    /// <summary>
    /// Stops the camera stream and releases the device (but does not dispose the service).
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Captures a single high-resolution still frame from the active camera.
    /// </summary>
    /// <returns>The captured frame as raw image bytes (JPEG).</returns>
    Task<byte[]> CaptureFrameAsync();

    /// <summary>
    /// Lists the names of all camera devices currently connected to the system.
    /// </summary>
    /// <returns>A read-only list of device display names, indexed by device ordinal.</returns>
    IReadOnlyList<string> EnumerateDevices();
}
