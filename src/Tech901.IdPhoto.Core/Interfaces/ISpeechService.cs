namespace Tech901.IdPhoto.Core.Interfaces;

public interface ISpeechService
{
    bool IsAvailable { get; }
    Task SpeakAsync(string text, CancellationToken ct = default);
    Task<string?> ListenAsync(TimeSpan? timeout = null, CancellationToken ct = default);
    Task PrepareListenAsync(CancellationToken ct = default);
}
