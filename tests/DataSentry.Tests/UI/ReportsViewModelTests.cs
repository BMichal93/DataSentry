using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Models;
using DataSentry.Tests.Fakes;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The Reports tab: the scans still in the database, and whichever one the user has opened. The purge
/// bounds the list; this class only has to read it back faithfully and open a report on request.
/// </summary>
[TestFixture]
public class ReportsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task LoadAsync_FillsTheListFromTheStore()
    {
        var store = new InMemoryScanResultStore();
        SeedReport(store, "C:/archive", [ResultAt("C:/archive/old-export.tmp", Recommendation.Delete)]);

        ReportsViewModel reports = Build(store);

        await reports.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reports.HasReports, Is.True);
            Assert.That(reports.Reports.Single().FolderPath, Is.EqualTo("C:/archive"));
            Assert.That(reports.Reports.Single().SummaryText, Is.EqualTo("1 file scanned, 1 suggested for deletion, nothing needs review."));
        });
    }

    [Test]
    public async Task LoadAsync_NothingInTheStore_SaysSoInsteadOfShowingAnEmptyList()
    {
        ReportsViewModel reports = Build(new InMemoryScanResultStore());

        await reports.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reports.HasReports, Is.False);
            Assert.That(reports.HasNoReports, Is.True);
        });
    }

    [Test]
    public async Task OpenReportAsync_ShowsTheReportExactlyAsItWasShownTheDayItRan()
    {
        var store = new InMemoryScanResultStore();
        SeedReport(store, "C:/work",
        [
            ResultAt("C:/work/export.tmp", Recommendation.Delete),
            ResultAt("C:/work/report.docx", Recommendation.Retain)
        ]);

        ReportsViewModel reports = Build(store);
        await reports.LoadAsync();

        await reports.OpenReportAsync(reports.Reports.Single());

        Assert.Multiple(() =>
        {
            Assert.That(reports.IsReportOpen, Is.True);
            Assert.That(reports.IsListOpen, Is.False);
            Assert.That(reports.Headline, Is.EqualTo("2 files scanned, 1 suggested for deletion, nothing needs review."));

            // The files behind the headline, straight from the store — no query happened at load time.
            Assert.That(reports.Results.Rows, Is.Not.Empty);
        });
    }

    [Test]
    public async Task CloseReport_PutsTheListBackWithoutLosingIt()
    {
        var store = new InMemoryScanResultStore();
        SeedReport(store, "C:/work", [ResultAt("C:/work/export.tmp", Recommendation.Delete)]);

        ReportsViewModel reports = Build(store);
        await reports.LoadAsync();
        await reports.OpenReportAsync(reports.Reports.Single());

        reports.CloseReport();

        Assert.Multiple(() =>
        {
            Assert.That(reports.IsListOpen, Is.True);
            Assert.That(reports.SelectedReport, Is.Null);
            Assert.That(reports.Reports, Has.Count.EqualTo(1), "closing a report does not forget the list");
        });
    }

    [Test]
    public async Task LoadAsync_AfterARefresh_NoReportIsLeftOpen()
    {
        // A refresh must never re-open a report the user did not just choose: the tab comes back on
        // the list, whatever was on screen before.
        var store = new InMemoryScanResultStore();
        SeedReport(store, "C:/work", [ResultAt("C:/work/export.tmp", Recommendation.Delete)]);

        ReportsViewModel reports = Build(store);
        await reports.LoadAsync();
        await reports.OpenReportAsync(reports.Reports.Single());

        await reports.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(reports.SelectedReport, Is.Null);
            Assert.That(reports.IsListOpen, Is.True);
        });
    }

    private static ReportsViewModel Build(InMemoryScanResultStore store) =>
        new(store, new ResultsViewModel(
            store,
            new FakeFileRecycler(),
            new FakeFileOpener(),
            new FakeConfirmationPrompt(answer: false),
            new FixedTimeProvider(Now)));

    private static void SeedReport(InMemoryScanResultStore store, string rootPath, IReadOnlyList<FileScanResult> results)
    {
        int deletable = results.Count(result => result.Recommendation == Recommendation.Delete);
        int needingReview = results.Count(result => result.Recommendation == Recommendation.Review);

        var report = new ScanReport(
            Guid.NewGuid(),
            rootPath,
            Now.AddDays(-3),
            Now.AddDays(-3),
            new ScanSummary(results.Count, 0, deletable, 0, needingReview),
            []);

        store.SaveReportAsync(report, results.ToAsyncEnumerable()).GetAwaiter().GetResult();
    }

    private static FileScanResult ResultAt(string filePath, Recommendation recommendation) =>
        new(
            filePath,
            SizeBytes: 4_096,
            CreatedUtc: Now,
            LastModifiedUtc: Now,
            LastAccessedUtc: Now,
            recommendation,
            RiskLevel.None,
            Reason: "Because the test said so",
            Findings: []);
}
