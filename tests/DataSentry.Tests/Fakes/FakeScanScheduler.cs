using System.Threading.Tasks;
using DataSentry.UI.Scheduling;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// The schedule, without Windows. Remembers the one scheduled scan the way Task Scheduler would,
/// which is all the view model ever asks of it.
/// </summary>
internal sealed class FakeScanScheduler : IScanScheduler
{
    public ScheduledScan? Scheduled { get; set; }

    public Task<ScheduledScan?> GetScheduledScanAsync() => Task.FromResult(Scheduled);

    public Task ScheduleDailyScanAsync(string folderPath, TimeOnly startTime)
    {
        Scheduled = new ScheduledScan(folderPath, startTime);

        return Task.CompletedTask;
    }

    public Task RemoveScheduledScanAsync()
    {
        Scheduled = null;

        return Task.CompletedTask;
    }
}
