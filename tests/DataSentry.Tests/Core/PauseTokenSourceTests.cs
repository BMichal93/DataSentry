using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Scanning;

namespace DataSentry.Tests.Core;

/// <summary>
/// The pause gate on its own. Its whole job is to let a waiter through at once while the scan runs, hold
/// it while the scan is paused, and let cancellation win over a pause every time — so those are the three
/// things these tests hold it to.
/// </summary>
[TestFixture]
public class PauseTokenSourceTests
{
    [Test]
    public void WaitWhilePaused_ADefaultToken_NeverPausesAndCompletesAtOnce()
    {
        // The counterpart of CancellationToken.None: a caller that wants no pause leaves the parameter
        // off, and pays nothing for it.
        PauseToken token = default;

        Assert.Multiple(() =>
        {
            Assert.That(token.IsPaused, Is.False);
            Assert.That(token.WaitWhilePausedAsync().IsCompletedSuccessfully, Is.True);
        });
    }

    [Test]
    public void WaitWhilePaused_WhileRunning_CompletesAtOnce()
    {
        var source = new PauseTokenSource();

        Assert.Multiple(() =>
        {
            Assert.That(source.IsPaused, Is.False);
            Assert.That(source.Token.WaitWhilePausedAsync().IsCompletedSuccessfully, Is.True);
        });
    }

    [Test]
    public async Task WaitWhilePaused_WhilePaused_DoesNotCompleteUntilResumed()
    {
        var source = new PauseTokenSource();
        source.Pause();

        Task waiting = source.Token.WaitWhilePausedAsync();

        Assert.That(source.IsPaused, Is.True);
        Assert.That(waiting.IsCompleted, Is.False, "a paused scan is held, not let through");

        source.Resume();

        // Completes now that the scan has resumed. Awaited rather than polled so the test is not timing-bound.
        await waiting;

        Assert.That(source.IsPaused, Is.False);
    }

    [Test]
    public void WaitWhilePaused_CancelledWhilePaused_Throws()
    {
        // The rule the whole feature rests on: Cancel is stronger than Pause. A scan held on the gate has
        // to stop when it is cancelled, not sit there for ever waiting for a resume that will never come.
        var source = new PauseTokenSource();
        source.Pause();

        using var cancellation = new CancellationTokenSource();
        Task waiting = source.Token.WaitWhilePausedAsync(cancellation.Token);

        cancellation.Cancel();

        Assert.That(async () => await waiting, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task WaitWhilePaused_ResumedThenPausedAgain_HoldsTheSecondTime()
    {
        // One waiter, parked on the gate across a whole pause/resume/pause cycle — the loop inside the
        // source has to cope with the scan being paused again after it was let through once.
        var source = new PauseTokenSource();

        source.Pause();
        source.Resume();
        await source.Token.WaitWhilePausedAsync();

        source.Pause();
        Task waitingAgain = source.Token.WaitWhilePausedAsync();

        Assert.That(waitingAgain.IsCompleted, Is.False);

        source.Resume();
        await waitingAgain;
    }

    [Test]
    public void PauseAndResume_CalledRedundantly_AreNoOps()
    {
        var source = new PauseTokenSource();

        Assert.DoesNotThrow(() => source.Resume(), "resuming a scan that is not paused does nothing");

        source.Pause();
        source.Pause();

        Assert.That(source.IsPaused, Is.True, "pausing an already-paused scan changes nothing");

        source.Resume();

        Assert.That(source.IsPaused, Is.False);
    }
}
