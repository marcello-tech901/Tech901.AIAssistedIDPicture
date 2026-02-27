using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.Infrastructure.Speech;

public sealed class NullSpeechService : ISpeechService
{
    public bool IsAvailable => false;

    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;

    public Task<string?> ListenAsync(TimeSpan? timeout = null, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task PrepareListenAsync(CancellationToken ct = default) => Task.CompletedTask;
}
