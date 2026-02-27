namespace Tech901.IdPhoto.Core.Interfaces;

public interface ICameraService : IDisposable
{
    bool IsRunning { get; }
    int FrameWidth { get; }
    int FrameHeight { get; }
    event EventHandler<byte[]>? FrameCaptured;
    Task StartAsync(int deviceIndex = 0, CancellationToken ct = default);
    Task StopAsync();
    Task<byte[]> CaptureFrameAsync();
    IReadOnlyList<string> EnumerateDevices();
}
