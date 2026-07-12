using System.Threading;
using System.Threading.Tasks;

namespace DataSentry.Core.Scanning;

/// <summary>
/// "Scan tonight at 22:00": turns a wall-clock time of day into the moment it next comes around, and
/// waits for that moment. It defers the start of a scan and nothing else — what the scan does when it
/// runs, and what the user must still confirm afterwards, are exactly as they are for a scan started
/// by hand.
/// </summary>
/// <remarks>
/// The user picks a local time, because "tonight at ten" is a local thought — but what comes back is an
/// instant, and instants compare in UTC like every timestamp that crosses into Core. The clock behind
/// both is the injected <see cref="TimeProvider"/>, which is what lets a test schedule for tonight and
/// have tonight arrive on demand.
/// </remarks>
public sealed class DelayedScanStart
{
    private readonly TimeProvider _timeProvider;

    public DelayedScanStart(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <summary>
    /// The next moment the given local time of day comes around: later today if it is still ahead,
    /// tomorrow if it has already passed. Returned with the local offset, so a caller can read the date
    /// off it for display; the instant it names is what <see cref="WaitUntilAsync"/> compares.
    /// </summary>
    public DateTimeOffset NextOccurrence(TimeOnly localStartTime)
    {
        DateTimeOffset localNow = _timeProvider.GetLocalNow();

        DateTime start = localNow.Date + localStartTime.ToTimeSpan();

        if (start <= localNow.DateTime)
        {
            start = start.AddDays(1);
        }

        return new DateTimeOffset(start, _timeProvider.LocalTimeZone.GetUtcOffset(start));
    }

    /// <summary>
    /// Waits until the clock reaches <paramref name="start"/>, or throws
    /// <see cref="OperationCanceledException"/> if the wait is called off first. Returns at once if the
    /// moment has already passed.
    /// </summary>
    /// <remarks>
    /// One timer armed for the whole distance, not a polling loop — but the clock gets the last word.
    /// A machine that sleeps through the evening does not run its timers while it is down, and a timer
    /// is only ever a claim about elapsed time anyway; so on every wake the clock is asked again, and
    /// the wait ends when the clock says the moment has arrived, not when a timer says enough time has
    /// gone by.
    /// </remarks>
    public async Task WaitUntilAsync(DateTimeOffset start, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining = start - _timeProvider.GetUtcNow();

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }
    }
}
