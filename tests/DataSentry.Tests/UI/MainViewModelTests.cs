using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Tests.Fakes;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The shell: two tabs and the choice between them. Everything either tab does is tested on its own
/// view model; what is tested here is only what the shell itself decides.
/// </summary>
[TestFixture]
public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void TheWindowOpens_OnTheSearchTab()
    {
        MainViewModel shell = BuildShell(new InMemoryScanResultStore());

        Assert.Multiple(() =>
        {
            Assert.That(shell.IsSearchTabOpen, Is.True);
            Assert.That(shell.IsReportsTabOpen, Is.False);
        });
    }

    [Test]
    public async Task ShowReportsAsync_OpensTheTabWithAListNoOlderThanTheClick()
    {
        // The report lands in the store after the shell is built — a scan that finished on the Search
        // tab, or headless from the Task Scheduler. Opening the tab must still find it.
        var store = new InMemoryScanResultStore();
        MainViewModel shell = BuildShell(store);

        await SeedReportAsync(store, "C:/work");

        await shell.ShowReportsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(shell.IsReportsTabOpen, Is.True);
            Assert.That(shell.Reports.Reports.Single().FolderPath, Is.EqualTo("C:/work"));
        });
    }

    [Test]
    public async Task ShowSearchCommand_ComesBackFromTheReportsTab()
    {
        MainViewModel shell = BuildShell(new InMemoryScanResultStore());

        await shell.ShowReportsAsync();
        shell.ShowSearchCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(shell.IsSearchTabOpen, Is.True);
            Assert.That(shell.IsReportsTabOpen, Is.False);
        });
    }

    private static MainViewModel BuildShell(InMemoryScanResultStore store)
    {
        var contentReader = new FakeFileContentReader(null);

        var scanEngine = new ScanEngine(
            new FakeFileSource([], null, null),
            contentReader,
            store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new DuplicateFileSweep(store, contentReader),
            new FixedTimeProvider(Now));

        var search = new SearchViewModel(
            scanEngine,
            BuildResults(store),
            new ScheduleViewModel(new FakeScanScheduler()),
            new FakeFolderPicker(null));

        return new MainViewModel(search, new ReportsViewModel(store, BuildResults(store)));
    }

    private static ResultsViewModel BuildResults(InMemoryScanResultStore store) =>
        new(
            store,
            new FakeFileRecycler(),
            new FakeFileOpener(),
            new FakeConfirmationPrompt(answer: false),
            new FixedTimeProvider(Now));

    private static Task SeedReportAsync(InMemoryScanResultStore store, string rootPath)
    {
        var report = new ScanReport(
            Guid.NewGuid(),
            rootPath,
            Now.AddDays(-1),
            Now.AddDays(-1),
            new ScanSummary(1, 0, 1, 0, 0),
            []);

        IReadOnlyList<FileScanResult> results =
        [
            new(
                $"{rootPath}/export.tmp",
                SizeBytes: 4_096,
                CreatedUtc: Now,
                LastModifiedUtc: Now,
                LastAccessedUtc: Now,
                Recommendation.Delete,
                RiskLevel.None,
                Reason: "Because the test said so",
                Findings: [])
        ];

        return store.SaveReportAsync(report, results.ToAsyncEnumerable());
    }
}
