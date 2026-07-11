using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Models;
using DataSentry.Tests.Fakes;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The detail list, on its own. Its one promise is that it never holds more than a page, however large
/// the scan was, and that is the promise these tests hold it to.
/// </summary>
[TestFixture]
public class ResultsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task LoadAsync_AMillionRowResult_ShowsOnePageAndNeverHoldsMore()
    {
        // A report far larger than any list could sensibly show. Pouring it into an ObservableCollection
        // would buffer the whole tree — the one thing the engine spent four branches refusing to do —
        // so the assertion is that the view model has exactly one page in hand, not a million rows.
        const int fileCount = 1_000_000;

        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(fileCount));

        var results = new ResultsViewModel(store);

        await results.LoadAsync(reportId, SummaryOf(deletable: fileCount));

        Assert.Multiple(() =>
        {
            Assert.That(results.Rows, Has.Count.EqualTo(ResultsViewModel.PageSize));
            Assert.That(results.PageDescription, Is.EqualTo("Showing 1–100 of 1,000,000 files."));
            Assert.That(results.HasNextPage, Is.True);
            Assert.That(results.HasPreviousPage, Is.False);
        });
    }

    [Test]
    public async Task LoadAsync_OpensOnTheFilesThatNeedADecisionWhenThereAreAny()
    {
        var store = new InMemoryScanResultStore();

        Guid reportId = SeedReport(store,
        [
            ResultAt("C:/work/a.tmp", Recommendation.Delete),
            ResultAt("C:/work/secrets.xlsx", Recommendation.Review),
            ResultAt("C:/work/keep.docx", Recommendation.Retain)
        ]);

        var results = new ResultsViewModel(store);

        await results.LoadAsync(reportId, new ScanSummary(3, 0, 1, 0, 1));

        Assert.Multiple(() =>
        {
            Assert.That(results.Shown, Is.EqualTo(Recommendation.Review));
            Assert.That(results.Rows.Select(row => row.FileName), Is.EqualTo(new[] { "secrets.xlsx" }));
        });
    }

    [Test]
    public async Task LoadAsync_NothingNeedsReview_OpensOnWhatCanBeDeletedInstead()
    {
        var store = new InMemoryScanResultStore();

        Guid reportId = SeedReport(store,
        [
            ResultAt("C:/work/a.tmp", Recommendation.Delete),
            ResultAt("C:/work/keep.docx", Recommendation.Retain)
        ]);

        var results = new ResultsViewModel(store);

        await results.LoadAsync(reportId, new ScanSummary(2, 0, 1, 0, 0));

        Assert.That(results.Shown, Is.EqualTo(Recommendation.Delete));
    }

    [Test]
    public async Task NextPage_ThenPrevious_WalksTheResultAPageAtATime()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(250));

        var results = new ResultsViewModel(store);
        await results.LoadAsync(reportId, SummaryOf(deletable: 250));

        await results.NextPageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(results.PageDescription, Is.EqualTo("Showing 101–200 of 250 files."));
            Assert.That(results.HasPreviousPage, Is.True);
            Assert.That(results.HasNextPage, Is.True);
        });

        await results.NextPageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(results.Rows, Has.Count.EqualTo(50));
            Assert.That(results.PageDescription, Is.EqualTo("Showing 201–250 of 250 files."));
            Assert.That(results.HasNextPage, Is.False);
        });
    }

    [Test]
    public async Task Show_NeverDisplaysTheMatchedPiiValue_OnlyItsTypeAndCount()
    {
        var store = new InMemoryScanResultStore();

        var finding = new PiiFinding(PiiCategory.Financial, "IBAN", MatchCount: 3, Confidence: 0.95);
        FileScanResult withPii = ResultAt("C:/work/suppliers.csv", Recommendation.Review) with
        {
            Findings = [finding]
        };

        Guid reportId = SeedReport(store, [withPii]);

        var results = new ResultsViewModel(store);
        await results.LoadAsync(reportId, new ScanSummary(1, 0, 0, 0, 1));

        FileRowViewModel row = results.Rows.Single();

        Assert.That(row.PiiSummary, Is.EqualTo("3 IBANs"));
    }

    private static IReadOnlyList<FileScanResult> DeleteResults(int count) =>
        Enumerable
            .Range(0, count)
            .Select(index => ResultAt($"C:/work/junk-{index}.tmp", Recommendation.Delete))
            .ToList();

    private static ScanSummary SummaryOf(int deletable) =>
        new(FilesScanned: deletable, TotalSizeBytes: 0, FilesRecommendedForDeletion: deletable, ReclaimableBytes: 0, FilesNeedingReview: 0);

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

    private static Guid SeedReport(InMemoryScanResultStore store, IReadOnlyList<FileScanResult> results)
    {
        var reportId = Guid.NewGuid();

        store.SaveReportAsync(
            new ScanReport(reportId, "C:/work", Now, Now, new ScanSummary(results.Count, 0, 0, 0, 0), []),
            AsAsyncEnumerable(results)).GetAwaiter().GetResult();

        return reportId;
    }

    private static async IAsyncEnumerable<FileScanResult> AsAsyncEnumerable(IReadOnlyList<FileScanResult> results)
    {
        foreach (FileScanResult result in results)
        {
            yield return result;
        }

        await Task.CompletedTask;
    }
}
