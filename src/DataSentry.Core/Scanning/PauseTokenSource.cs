using System.Threading;
using System.Threading.Tasks;

namespace DataSentry.Core.Scanning;

/// <summary>
/// The controlling half of a pause: whoever holds this can pause and resume a scan, and hands its
/// <see cref="Token"/> to the scan itself. The split mirrors <see cref="CancellationTokenSource"/> and
/// <see cref="CancellationToken"/> for the same reason — the view model that drives the scan keeps the
/// controls, and the engine gets only the ability to wait.
/// </summary>
public sealed class PauseTokenSource
{
    private readonly object _gate = new();

    /// <summary>
    /// Null while the scan runs; a fresh, uncompleted signal while it is paused. Resuming completes it,
    /// which is what releases every waiter at the pause gate. Volatile so the running scan sees a pause
    /// without taking the lock on its hot path.
    /// </summary>
    private volatile TaskCompletionSource<bool>? _resumeSignal;

    public bool IsPaused => _resumeSignal is not null;

    public PauseToken Token => new(this);

    /// <summary>Holds the scan at its next unit of work. A second call while already paused does nothing.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _resumeSignal ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>Lets a paused scan carry on. A call while not paused does nothing.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            TaskCompletionSource<bool>? signal = _resumeSignal;
            _resumeSignal = null;
            signal?.SetResult(true);
        }
    }

    internal async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        // A loop, not a single await, because a scan can be paused, resumed and paused again while one
        // waiter is parked here, and because a cancellation has to win over the wait every time round.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource<bool>? signal = _resumeSignal;

            if (signal is null)
            {
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(signal.Task, cancelled.Task);
            }
        }
    }
}
