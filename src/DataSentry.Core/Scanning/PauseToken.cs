using System.Threading;
using System.Threading.Tasks;

namespace DataSentry.Core.Scanning;

/// <summary>
/// The read-only half of a pause: something a scan can wait on between units of work, so that a paused
/// scan stops where it is instead of racing to the end. Deliberately shaped like
/// <see cref="CancellationToken"/> — the engine is handed the token and can only <i>wait</i> on it,
/// never pause anything itself, exactly as it can only observe cancellation and never request it.
/// </summary>
/// <remarks>
/// A <see langword="default"/> token is one that never pauses, the counterpart of a
/// <see cref="CancellationToken.None"/>: it lets a caller — a headless scheduled scan, a test — leave
/// the pause parameter off and pay nothing for a feature it does not use.
/// </remarks>
public readonly struct PauseToken
{
    private readonly PauseTokenSource? _source;

    internal PauseToken(PauseTokenSource source) => _source = source;

    /// <summary>Whether the scan is paused right now.</summary>
    public bool IsPaused => _source?.IsPaused ?? false;

    /// <summary>
    /// Returns at once while the scan is running, and blocks until it is resumed while it is paused.
    /// Honours <paramref name="cancellationToken"/> throughout: a scan cancelled while paused stops,
    /// which is what keeps Cancel the stronger of the two — Pause holds a scan, Cancel ends it.
    /// </summary>
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default) =>
        _source?.WaitWhilePausedAsync(cancellationToken) ?? Task.CompletedTask;
}
