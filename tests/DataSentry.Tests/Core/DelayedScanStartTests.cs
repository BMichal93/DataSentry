using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Scanning;
using Microsoft.Extensions.Time.Testing;

namespace DataSentry.Tests.Core;

/// <summary>
/// The delayed start, on a clock the test moves by hand. "Tonight at ten" arrives on demand, an
/// eight-hour wait costs nothing, and a machine sleeping through the moment is one method call.
/// </summary>
[TestFixture]
public class DelayedScanStartTests
{
    /// <summary>A Tuesday morning, 09:00 UTC. The tests speak local time; the zone is set per test.</summary>
    private static readonly DateTimeOffset TuesdayMorningUtc = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void NextOccurrence_TimeStillAheadToday_IsToday()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        DateTimeOffset start = delayedStart.NextOccurrence(new TimeOnly(22, 0));

        Assert.That(start, Is.EqualTo(new DateTimeOffset(2026, 7, 7, 22, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void NextOccurrence_TimeAlreadyPassedToday_RollsToTomorrow()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        DateTimeOffset start = delayedStart.NextOccurrence(new TimeOnly(7, 0));

        Assert.That(start, Is.EqualTo(new DateTimeOffset(2026, 7, 8, 7, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void NextOccurrence_ExactlyTheCurrentMoment_RollsToTomorrow()
    {
        // "At 09:00", asked at 09:00:00, is a moment that has already begun — starting a scan for it
        // now would surprise the user who typed a time expecting a wait.
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        DateTimeOffset start = delayedStart.NextOccurrence(new TimeOnly(9, 0));

        Assert.That(start, Is.EqualTo(new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void NextOccurrence_TheTimeIsLocal_AndTheInstantIsNot()
    {
        // The user two hours east of Greenwich says "21:00" and means their evening. The moment that
        // names is 19:00 UTC — and it is the instant, not the wall-clock digits, that the wait compares.
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc, utcOffsetHours: 2);
        var delayedStart = new DelayedScanStart(clock);

        DateTimeOffset start = delayedStart.NextOccurrence(new TimeOnly(21, 0));

        Assert.Multiple(() =>
        {
            Assert.That(start.TimeOfDay, Is.EqualTo(new TimeSpan(21, 0, 0)));
            Assert.That(start.UtcDateTime, Is.EqualTo(new DateTime(2026, 7, 7, 19, 0, 0)));
        });
    }

    [Test]
    public void WaitUntilAsync_BeforeTheMoment_IsStillWaiting()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        Task waiting = delayedStart.WaitUntilAsync(TuesdayMorningUtc.AddHours(13), CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(12));

        Assert.That(waiting.IsCompleted, Is.False);
    }

    [Test]
    public async Task WaitUntilAsync_WhenTheClockReachesTheMoment_Completes()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        Task waiting = delayedStart.WaitUntilAsync(TuesdayMorningUtc.AddHours(13), CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(13));

        await waiting;

        Assert.That(waiting.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task WaitUntilAsync_MachineSleptPastTheMoment_CompletesOnWake()
    {
        // A laptop closed at 09:00 and opened at midnight has no timers to thank for anything — the
        // wait asks the clock again on wake, sees the moment has passed, and lets the scan run.
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        Task waiting = delayedStart.WaitUntilAsync(TuesdayMorningUtc.AddHours(13), CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(15));

        await waiting;

        Assert.That(waiting.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task WaitUntilAsync_MomentAlreadyPassed_ReturnsAtOnce()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);

        Task waiting = delayedStart.WaitUntilAsync(TuesdayMorningUtc.AddHours(-1), CancellationToken.None);

        await waiting;

        Assert.That(waiting.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void WaitUntilAsync_CalledOff_ThrowsAndStopsWaiting()
    {
        FakeTimeProvider clock = ClockAt(TuesdayMorningUtc);
        var delayedStart = new DelayedScanStart(clock);
        using var callOff = new CancellationTokenSource();

        Task waiting = delayedStart.WaitUntilAsync(TuesdayMorningUtc.AddHours(13), callOff.Token);

        callOff.Cancel();

        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => waiting);
    }

    private static FakeTimeProvider ClockAt(DateTimeOffset utcNow, int utcOffsetHours = 0)
    {
        var clock = new FakeTimeProvider(utcNow);

        clock.SetLocalTimeZone(utcOffsetHours == 0
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.CreateCustomTimeZone(
                $"UTC+{utcOffsetHours}", TimeSpan.FromHours(utcOffsetHours), $"UTC+{utcOffsetHours}", $"UTC+{utcOffsetHours}"));

        return clock;
    }
}
