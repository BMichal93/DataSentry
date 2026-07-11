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

        ResultsViewModel results = Build(store);

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

        ResultsViewModel results = Build(store);

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

        ResultsViewModel results = Build(store);

        await results.LoadAsync(reportId, new ScanSummary(2, 0, 1, 0, 0));

        Assert.That(results.Shown, Is.EqualTo(Recommendation.Delete));
    }

    [Test]
    public async Task NextPage_ThenPrevious_WalksTheResultAPageAtATime()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(250));

        ResultsViewModel results = Build(store);
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

        ResultsViewModel results = Build(store);
        await results.LoadAsync(reportId, new ScanSummary(1, 0, 0, 0, 1));

        FileRowViewModel row = results.Rows.Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.PiiSummary, Is.EqualTo("3 IBANs"));

            // The row explains why the finding is dangerous — the kind of data and the exposure — but
            // the matched values themselves have no way onto the screen: the model never carried them.
            Assert.That(row.WhyItMatters, Does.StartWith("Bank account or card numbers."));
        });
    }

    [Test]
    public async Task Show_AFileWithSeveralKindsOfFindings_ExplainsTheWorstOneFirst()
    {
        var store = new InMemoryScanResultStore();

        FileScanResult withMixedPii = ResultAt("C:/hr/medical-leave.xlsx", Recommendation.Review) with
        {
            Findings =
            [
                new PiiFinding(PiiCategory.Contact, "email address", MatchCount: 12, Confidence: 0.7),
                new PiiFinding(PiiCategory.SpecialCategory, "health term", MatchCount: 4, Confidence: 0.6)
            ]
        };

        Guid reportId = SeedReport(store, [withMixedPii]);

        ResultsViewModel results = Build(store);
        await results.LoadAsync(reportId, new ScanSummary(1, 0, 0, 0, 1));

        // Special category data is why this file needs a human — so it is the first thing said about
        // it, in the same priority order the recommendation itself was decided by.
        Assert.That(results.Rows.Single().WhyItMatters, Does.StartWith("Likely health, beliefs or other special-category data"));
    }

    [Test]
    public async Task DeleteSuggested_AsksInPlainWordsWithTheRealNumberBeforeAnythingIsDestroyed()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(12));

        var recycler = new FakeFileRecycler();
        var confirmation = new FakeConfirmationPrompt(answer: true);

        ResultsViewModel results = Build(store, recycler, confirmation);
        await results.LoadAsync(reportId, SummaryOf(deletable: 12));

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                confirmation.LastQuestion,
                Is.EqualTo("Send 12 files to the recycle bin?"),
                "the number is what makes it a decision rather than a leap of faith");

            Assert.That(recycler.Recycled, Has.Count.EqualTo(12));
            Assert.That(results.DeletionOutcome, Is.EqualTo("12 files sent to the recycle bin."));
        });
    }

    [Test]
    public async Task DeleteSuggested_TheUserSaysNo_DestroysNothingAtAll()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(12));

        var recycler = new FakeFileRecycler();

        ResultsViewModel results = Build(store, recycler, new FakeConfirmationPrompt(answer: false));
        await results.LoadAsync(reportId, SummaryOf(deletable: 12));

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(recycler.RecycleAttempts, Is.Empty, "nothing is deleted without explicit confirmation");
            Assert.That(results.DeletableCount, Is.EqualTo(12), "and the files are all still there to delete");
        });
    }

    [Test]
    public async Task DeleteSuggested_AFileTheUserUnticked_IsSparedAndTheRestStillGo()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(3));

        var recycler = new FakeFileRecycler();
        var confirmation = new FakeConfirmationPrompt(answer: true);

        ResultsViewModel results = Build(store, recycler, confirmation);
        await results.LoadAsync(reportId, SummaryOf(deletable: 3));

        // The exclusion model: every deletable row arrives ticked, and unticking one spares it. The user
        // is confirming a recommendation, not assembling one out of ten thousand checkboxes.
        FileRowViewModel spared = results.Rows.Single(row => row.FileName == "junk-1.tmp");
        spared.IsSelectedForDeletion = false;

        Assert.That(results.DeletableCount, Is.EqualTo(2), "the count follows the ticks, before anything is asked");

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(confirmation.LastQuestion, Is.EqualTo("Send 2 files to the recycle bin?"));

            Assert.That(
                recycler.Recycled,
                Is.EqualTo(new[] { "C:/work/junk-0.tmp", "C:/work/junk-2.tmp" }),
                "the unticked file was never even offered to the recycler");
        });
    }

    [Test]
    public async Task DeleteSuggested_AFileUntickedOnOnePage_IsStillSparedAfterPagingAwayAndBack()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(150));

        var recycler = new FakeFileRecycler();

        ResultsViewModel results = Build(store, recycler, new FakeConfirmationPrompt(answer: true));
        await results.LoadAsync(reportId, SummaryOf(deletable: 150));

        results.Rows.Single(row => row.FileName == "junk-7.tmp").IsSelectedForDeletion = false;

        // The rows are rebuilt from the store on every page turn, so a decision remembered on a row
        // would be forgotten here. It is remembered on the view model instead.
        await results.NextPageAsync();
        await results.PreviousPageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(results.Rows.Single(row => row.FileName == "junk-7.tmp").IsSelectedForDeletion, Is.False);
            Assert.That(results.DeletableCount, Is.EqualTo(149));
        });

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(recycler.Recycled, Has.Count.EqualTo(149));
            Assert.That(recycler.RecycleAttempts, Has.None.EqualTo("C:/work/junk-7.tmp"));
        });
    }

    [Test]
    public async Task DeleteSuggested_AFileThatWillNotGo_IsCountedAndListedWithoutStoppingTheRest()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(5));

        var recycler = new FakeFileRecycler();
        recycler.Refuse("C:/work/junk-2.tmp", "It is open in another program.");

        ResultsViewModel results = Build(store, recycler, new FakeConfirmationPrompt(answer: true));
        await results.LoadAsync(reportId, SummaryOf(deletable: 5));

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            // A locked file is reported exactly as an unreadable one is during a scan: counted, named,
            // and never a reason to abandon the other four.
            Assert.That(recycler.Recycled, Has.Count.EqualTo(4));
            Assert.That(results.HasDeletionFailures, Is.True);

            Assert.That(results.DeletionFailures.Single().FilePath, Is.EqualTo("C:/work/junk-2.tmp"));
            Assert.That(results.DeletionFailures.Single().Reason, Is.EqualTo("It is open in another program."));

            Assert.That(
                results.DeletionOutcome,
                Is.EqualTo("4 files sent to the recycle bin. 1 file could not be deleted."));

            // It would not go, so it is still there to try again — the report has not pretended otherwise.
            Assert.That(results.DeletableCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task DeleteSuggested_AFileThatNeedsReview_CanNeverReachTheRecyclerEvenWhenAskedTo()
    {
        var store = new InMemoryScanResultStore();

        Guid reportId = SeedReport(store,
        [
            ResultAt("C:/hr/medical-leave.xlsx", Recommendation.Review),
            ResultAt("C:/work/payroll.xlsx", Recommendation.Retain),
            ResultAt("C:/work/junk.tmp", Recommendation.Delete)
        ]);

        var recycler = new FakeFileRecycler();

        ResultsViewModel results = Build(store, recycler, new FakeConfirmationPrompt(answer: true));
        await results.LoadAsync(reportId, new ScanSummary(3, 0, 1, 0, 1));

        // The user is looking at the review heading, where the personal data is, and asks to delete. The
        // rows here carry no checkbox — but the test does not go through the checkbox, it goes straight
        // at the method, because the guarantee has to hold even if the view is wrong.
        await results.ShowAsync(Recommendation.Review);
        await results.DeleteSuggestedAsync();

        Assert.That(recycler.RecycleAttempts, Is.Empty, "a Review file is not the user's to delete on a scan's say-so");

        // And on the heading where deleting is legitimate, only the condemned file goes: the review file
        // and the retained one are unreachable from here, because the paths come from the store's
        // pending-deletion query and not from the screen.
        await results.ShowAsync(Recommendation.Delete);
        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(recycler.Recycled, Is.EqualTo(new[] { "C:/work/junk.tmp" }));

            Assert.That(
                results.Rows.Where(row => row.CanBeDeleted).Select(row => row.FileName),
                Is.Empty,
                "and once it has gone there is nothing left on this heading to delete");
        });
    }

    [Test]
    public async Task DeleteSuggested_AfterTheFilesHaveGone_TheRowsStopSayingDeleteAsIfNothingHappened()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, DeleteResults(2));

        ResultsViewModel results = Build(store, new FakeFileRecycler(), new FakeConfirmationPrompt(answer: true));
        await results.LoadAsync(reportId, SummaryOf(deletable: 2));

        await results.DeleteSuggestedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                results.Rows.Select(row => row.RecommendationText),
                Is.All.EqualTo("Sent to recycle bin"),
                "a row still saying Delete tells the user their decision did not take");

            Assert.That(results.Rows, Has.None.Matches<FileRowViewModel>(row => row.CanBeDeleted));
            Assert.That(results.DeletableCount, Is.Zero);
            Assert.That(results.CanDelete, Is.False);
        });

        // And it is the report that remembers, not the screen: reopened tomorrow, the row still says so.
        FileScanResult stored = store.ResultsOf(reportId).First();

        Assert.That(stored.RecycledUtc, Is.EqualTo(Now));
    }

    [Test]
    public async Task Open_ARowThatNeedsReview_OpensTheFileItself()
    {
        var store = new InMemoryScanResultStore();
        Guid reportId = SeedReport(store, [ResultAt("C:/hr/medical-leave.xlsx", Recommendation.Review)]);

        var opener = new FakeFileOpener();

        ResultsViewModel results = Build(store, fileOpener: opener);
        await results.LoadAsync(reportId, new ScanSummary(1, 0, 0, 0, 1));

        results.Rows.Single().OpenCommand.Execute(null);

        // The compliant way to inspect a flagged file: the report never prints the personal data, so the
        // user reads it where it already lives, behind the access controls that already guard it.
        Assert.That(opener.Opened, Is.EqualTo(new[] { "C:/hr/medical-leave.xlsx" }));
    }

    private static ResultsViewModel Build(
        InMemoryScanResultStore store,
        FakeFileRecycler? recycler = null,
        FakeConfirmationPrompt? confirmation = null,
        FakeFileOpener? fileOpener = null) =>
        new(
            store,
            recycler ?? new FakeFileRecycler(),
            fileOpener ?? new FakeFileOpener(),
            confirmation ?? new FakeConfirmationPrompt(answer: false),
            new FixedTimeProvider(Now));

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
