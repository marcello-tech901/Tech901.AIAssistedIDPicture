using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.Infrastructure.Speech;

/// <summary>
/// A no-op implementation of <see cref="ISpeechService"/> used when Azure Speech
/// credentials are not configured.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the <strong>Null Object Pattern</strong>: instead of returning
/// <c>null</c> from the DI container when Speech is unavailable, the container provides
/// this safe, do-nothing implementation. All consumers can call <see cref="ISpeechService"/>
/// methods without null-checks — the contract is always satisfied.
/// </para>
/// <para>
/// AI-102: Graceful degradation is a best practice for AI-powered features. The kiosk
/// remains fully functional without speech — participants simply type their names
/// instead of speaking them. No code paths need to check "is speech available?" because
/// this null object absorbs all calls silently.
/// </para>
/// </remarks>
public sealed class NullSpeechService : ISpeechService
{
    /// <inheritdoc />
    /// <remarks>Always returns <c>false</c> — no Azure Speech credentials are available.</remarks>
    public bool IsAvailable => false;

    /// <inheritdoc />
    /// <remarks>No-op: returns immediately without producing any audio output.</remarks>
    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>No-op: returns <c>null</c> immediately, indicating no speech was recognized.</remarks>
    public Task<string?> ListenAsync(TimeSpan? timeout = null, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc />
    /// <remarks>No-op: nothing to pre-warm when there is no recognizer to prepare.</remarks>
    public Task PrepareListenAsync(CancellationToken ct = default) => Task.CompletedTask;
}
