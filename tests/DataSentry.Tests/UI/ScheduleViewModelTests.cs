using System.Threading.Tasks;
using DataSentry.Tests.Fakes;
using DataSentry.UI.Scheduling;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The scheduled scan, exercised without Windows: the view model talks to <see cref="IScanScheduler"/>
/// and has no idea a Task Scheduler exists, so neither does this fixture.
/// </summary>
[TestFixture]
public class ScheduleViewModelTests
{
    [Test]
    public async Task ScheduleDailyAsync_AWellFormedTime_SchedulesTheScanAndSaysSo()
    {
        var scheduler = new FakeScanScheduler();
        var viewModel = new ScheduleViewModel(scheduler);

        viewModel.StartTimeText = "16:30";

        await viewModel.ScheduleDailyAsync("C:/work");

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.Scheduled, Is.EqualTo(new ScheduledScan("C:/work", new TimeOnly(16, 30))));
            Assert.That(viewModel.HasSchedule, Is.True);
            Assert.That(viewModel.Description, Is.EqualTo("Scans C:/work every day at 16:30."));
            Assert.That(viewModel.Message, Is.Empty);
        });
    }

    [Test]
    public async Task ScheduleDailyAsync_TheDefaultTime_IsTheEndOfAWorkday()
    {
        // "Just before I leave work" is the use case the feature was asked for, so the box must not
        // open on midnight or on empty.
        var scheduler = new FakeScanScheduler();
        var viewModel = new ScheduleViewModel(scheduler);

        await viewModel.ScheduleDailyAsync("C:/work");

        Assert.That(scheduler.Scheduled?.StartTime, Is.EqualTo(new TimeOnly(17, 0)));
    }

    [Test]
    public async Task ScheduleDailyAsync_SomethingThatIsNotATime_SchedulesNothingAndExplains()
    {
        var scheduler = new FakeScanScheduler();
        var viewModel = new ScheduleViewModel(scheduler);

        viewModel.StartTimeText = "half past nine";

        await viewModel.ScheduleDailyAsync("C:/work");

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.Scheduled, Is.Null);
            Assert.That(viewModel.HasSchedule, Is.False);
            Assert.That(viewModel.Message, Does.Contain("not a time"));
        });
    }

    [Test]
    public async Task RemoveAsync_AnExistingSchedule_IsGoneFromWindowsAndFromTheScreen()
    {
        var scheduler = new FakeScanScheduler();
        var viewModel = new ScheduleViewModel(scheduler);

        await viewModel.ScheduleDailyAsync("C:/work");
        await viewModel.RemoveAsync();

        Assert.Multiple(() =>
        {
            Assert.That(scheduler.Scheduled, Is.Null);
            Assert.That(viewModel.HasSchedule, Is.False);
            Assert.That(viewModel.Description, Is.Empty);
        });
    }

    [Test]
    public async Task LoadAsync_AScheduleSetInAnEarlierSession_IsShownAgain()
    {
        var scheduler = new FakeScanScheduler
        {
            Scheduled = new ScheduledScan("C:/archive", new TimeOnly(7, 15))
        };

        var viewModel = new ScheduleViewModel(scheduler);

        await viewModel.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasSchedule, Is.True);
            Assert.That(viewModel.Description, Is.EqualTo("Scans C:/archive every day at 07:15."));
        });
    }
}
