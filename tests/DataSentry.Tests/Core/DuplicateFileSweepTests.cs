using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Tests.Fakes;

namespace DataSentry.Tests.Core;

/// <summary>
/// The sweep, against results that have already been written down — which is the only state of the
/// world it ever sees. Two things are tested throughout: the verdict it reaches, and what it had to
/// read to reach it. The second matters as much as the first. A sweep that hashed the whole tree would
/// get every one of these answers right and would still be wrong.
/// </summary>
[TestFixture]
public class DuplicateFileSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    private InMemoryScanResultStore _store = null!;
    private Guid _reportId;
    private List<ScanError> _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryScanResultStore();
        _reportId = Guid.NewGuid();
        _errors = [];
    }

    [Test]
    public async Task SweepAsync_TwoFilesOfDifferentSizes_NeverOpensEitherOfThem()
    {
        FakeFileContentReader contentReader = await GivenResults(
        [
            FileOf("C:/work/invoice.pdf", sizeBytes: 1_000),
            FileOf("C:/work/contract.pdf", sizeBytes: 2_000)
        ]);

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                contentReader.HashedPaths,
                Is.Empty,
                "two files of different sizes cannot be copies, and the size alone says so — nothing should have been read");
            Assert.That(sweep.FilesMarkedForDeletion, Is.Zero);
            Assert.That(RecommendationFor("C:/work/invoice.pdf"), Is.EqualTo(Recommendation.Retain));
        });
    }

    [Test]
    public async Task SweepAsync_TwoFilesOfTheSameSizeButDifferentContents_KeepsBoth()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/january.xlsx", sizeBytes: 4_096),
                FileOf("C:/work/february.xlsx", sizeBytes: 4_096)
            ],
            contentHashByPath: new Dictionary<string, string>
            {
                ["C:/work/january.xlsx"] = "january-contents",
                ["C:/work/february.xlsx"] = "february-contents"
            });

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                contentReader.HashedPaths,
                Is.EquivalentTo(new[] { "C:/work/january.xlsx", "C:/work/february.xlsx" }),
                "the same size is only a suspicion, so both had to be read for the hash to settle it");
            Assert.That(
                sweep.FilesMarkedForDeletion,
                Is.Zero,
                "two spreadsheets that happen to weigh the same are not the same spreadsheet");
            Assert.That(RecommendationFor("C:/work/january.xlsx"), Is.EqualTo(Recommendation.Retain));
            Assert.That(RecommendationFor("C:/work/february.xlsx"), Is.EqualTo(Recommendation.Retain));
        });
    }

    [Test]
    public async Task SweepAsync_ThreeIdenticalFiles_KeepsTheOldestAndCondemnsTheOtherTwo()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/copies/report (2).xlsx", sizeBytes: 4_096, createdUtc: Now.AddDays(-10)),
                FileOf("C:/work/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddYears(-3)),
                FileOf("C:/work/backup/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddDays(-2))
            ],
            contentHashByPath: AllTheSame(
                "C:/work/copies/report (2).xlsx",
                "C:/work/report.xlsx",
                "C:/work/backup/report.xlsx"));

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                RecommendationFor("C:/work/report.xlsx"),
                Is.EqualTo(Recommendation.Retain),
                "the original is the one that existed before the copies were made of it");
            Assert.That(RecommendationFor("C:/work/copies/report (2).xlsx"), Is.EqualTo(Recommendation.Delete));
            Assert.That(RecommendationFor("C:/work/backup/report.xlsx"), Is.EqualTo(Recommendation.Delete));

            Assert.That(sweep.FilesMarkedForDeletion, Is.EqualTo(2));
            Assert.That(sweep.ReclaimableBytes, Is.EqualTo(8_192), "deleting two of the three gives back two of the three");
        });
    }

    [Test]
    public async Task SweepAsync_ACopyItCondemns_IsToldWhichFileIsBeingKeptInstead()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddYears(-3)),
                FileOf("C:/work/backup/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddDays(-2))
            ],
            contentHashByPath: AllTheSame("C:/work/report.xlsx", "C:/work/backup/report.xlsx"));

        await SweepAsync(contentReader);

        Assert.That(ReasonFor("C:/work/backup/report.xlsx"), Is.EqualTo("Identical copy of report.xlsx, which is kept"));
    }

    [Test]
    public async Task SweepAsync_EmptyFiles_AreNeverHashedAndKeepTheReasonTheyWereAlreadyCondemnedFor()
    {
        FakeFileContentReader contentReader = await GivenResults(
        [
            FileOf("C:/work/new.txt", sizeBytes: 0, recommendation: Recommendation.Delete, reason: "Empty file"),
            FileOf("C:/work/also-new.txt", sizeBytes: 0, recommendation: Recommendation.Delete, reason: "Empty file")
        ]);

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                contentReader.HashedPaths,
                Is.Empty,
                "every empty file is identical to every other one, and reading them all to prove it would buy nothing");
            Assert.That(
                sweep.FilesMarkedForDeletion,
                Is.Zero,
                "they are already condemned, and counting them again would double the headline");
            Assert.That(
                ReasonFor("C:/work/also-new.txt"),
                Is.EqualTo("Empty file"),
                "'Empty file' is a better answer than 'Identical copy of', and it was true first");
        });
    }

    [Test]
    public async Task SweepAsync_CopyHoldingSpecialCategoryData_IsSurfacedForReviewRatherThanDeleted()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf(
                    "C:/work/medical-notes.xlsx",
                    sizeBytes: 4_096,
                    createdUtc: Now.AddYears(-3),
                    recommendation: Recommendation.Review,
                    findings: [new PiiFinding(PiiCategory.SpecialCategory, "special category term", 4, 0.9)]),
                FileOf(
                    "C:/work/backup/medical-notes.xlsx",
                    sizeBytes: 4_096,
                    createdUtc: Now.AddDays(-2),
                    recommendation: Recommendation.Review,
                    reason: "Special category personal data — needs a human decision (4 special category terms)",
                    findings: [new PiiFinding(PiiCategory.SpecialCategory, "special category term", 4, 0.9)])
            ],
            contentHashByPath: AllTheSame("C:/work/medical-notes.xlsx", "C:/work/backup/medical-notes.xlsx"));

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                RecommendationFor("C:/work/backup/medical-notes.xlsx"),
                Is.EqualTo(Recommendation.Review),
                "the duplicate of a file full of health data is a file full of health data — a finding overrides a delete, and being a copy is no exception to that");
            Assert.That(
                ReasonFor("C:/work/backup/medical-notes.xlsx"),
                Does.StartWith("Special category personal data"),
                "and the user is told why it needs them, not that it happens to exist twice");
            Assert.That(sweep.FilesMarkedForDeletion, Is.Zero);
        });
    }

    [Test]
    public async Task SweepAsync_CopyHoldingOrdinaryPersonalDataStillInUse_IsKept()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/contacts.csv", sizeBytes: 4_096, createdUtc: Now.AddYears(-3)),
                FileOf(
                    "C:/work/backup/contacts.csv",
                    sizeBytes: 4_096,
                    createdUtc: Now.AddDays(-2),
                    recommendation: Recommendation.Retain,
                    reason: "In use, and it holds personal data (12 email addresses)",
                    findings: [new PiiFinding(PiiCategory.Contact, "email address", 12, 0.9)])
            ],
            contentHashByPath: AllTheSame("C:/work/contacts.csv", "C:/work/backup/contacts.csv"));

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                RecommendationFor("C:/work/backup/contacts.csv"),
                Is.EqualTo(Recommendation.Retain),
                "personal data is never deleted on this tool's own say-so, and a duplicate verdict is still its own say-so");
            Assert.That(sweep.FilesMarkedForDeletion, Is.Zero);
        });
    }

    [Test]
    public async Task SweepAsync_CopyLockedByAnotherProcess_IsReportedAndLeftWithTheVerdictItHad()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddYears(-3)),
                FileOf("C:/work/open-in-excel.xlsx", sizeBytes: 4_096, createdUtc: Now.AddDays(-2))
            ],
            contentHashByPath: AllTheSame("C:/work/report.xlsx", "C:/work/open-in-excel.xlsx"),
            unreadablePaths: new HashSet<string> { "C:/work/open-in-excel.xlsx" });

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(
                RecommendationFor("C:/work/open-in-excel.xlsx"),
                Is.EqualTo(Recommendation.Retain),
                "a copy that could not be read was never proved to be a copy, and unproven is not condemned");
            Assert.That(_errors.Single().Path, Is.EqualTo("C:/work/open-in-excel.xlsx"));
            Assert.That(sweep.FilesMarkedForDeletion, Is.Zero);
        });
    }

    [Test]
    public async Task SweepAsync_IdenticalFilesCreatedInTheSameInstant_KeepsTheSameOneOnEveryRun()
    {
        // A folder copied wholesale gives every file in it the same creation time, so the tie has to be
        // broken by something — and by something that does not depend on the order the disk hands them over.
        Dictionary<string, string> sameContents = AllTheSame("C:/work/b-copy.xlsx", "C:/work/a-original.xlsx");

        FakeFileContentReader firstRun = await GivenResults(
            [
                FileOf("C:/work/b-copy.xlsx", sizeBytes: 4_096, createdUtc: Now),
                FileOf("C:/work/a-original.xlsx", sizeBytes: 4_096, createdUtc: Now)
            ],
            contentHashByPath: sameContents);

        await SweepAsync(firstRun);

        Assert.Multiple(() =>
        {
            Assert.That(RecommendationFor("C:/work/a-original.xlsx"), Is.EqualTo(Recommendation.Retain));
            Assert.That(RecommendationFor("C:/work/b-copy.xlsx"), Is.EqualTo(Recommendation.Delete));
        });
    }

    [Test]
    public async Task SweepAsync_CopyAlreadyCondemnedAsJunk_IsNotCountedTwiceTowardsTheHeadline()
    {
        FakeFileContentReader contentReader = await GivenResults(
            [
                FileOf("C:/work/report.xlsx", sizeBytes: 4_096, createdUtc: Now.AddYears(-3)),
                FileOf(
                    "C:/work/report.bak",
                    sizeBytes: 4_096,
                    createdUtc: Now.AddDays(-2),
                    recommendation: Recommendation.Delete,
                    reason: "Backup file (.bak)")
            ],
            contentHashByPath: AllTheSame("C:/work/report.xlsx", "C:/work/report.bak"));

        DuplicateSweepResult sweep = await SweepAsync(contentReader);

        Assert.Multiple(() =>
        {
            Assert.That(RecommendationFor("C:/work/report.bak"), Is.EqualTo(Recommendation.Delete));
            Assert.That(ReasonFor("C:/work/report.bak"), Is.EqualTo("Backup file (.bak)"));
            Assert.That(
                sweep.FilesMarkedForDeletion,
                Is.Zero,
                "the summary already counted it when it went past as junk, and one file is not two files");
        });
    }

    private async Task<DuplicateSweepResult> SweepAsync(FakeFileContentReader contentReader) =>
        await new DuplicateFileSweep(_store, contentReader).SweepAsync(_reportId, _errors.Add);

    /// <summary>The results of a scan that has already run — which is all the sweep is ever given.</summary>
    private async Task<FakeFileContentReader> GivenResults(
        IReadOnlyList<FileScanResult> results,
        IReadOnlyDictionary<string, string>? contentHashByPath = null,
        IReadOnlySet<string>? unreadablePaths = null)
    {
        var report = new ScanReport(_reportId, "C:/work", Now, Now, new ScanSummary(0, 0, 0, 0, 0), []);

        await _store.SaveReportAsync(report, results.ToAsyncEnumerable());

        return new FakeFileContentReader(
            textByPath: null,
            unreadablePaths: unreadablePaths,
            contentHashByPath: contentHashByPath);
    }

    private static Dictionary<string, string> AllTheSame(params string[] filePaths) =>
        filePaths.ToDictionary(filePath => filePath, _ => "identical-contents", StringComparer.Ordinal);

    private Recommendation RecommendationFor(string filePath) => ResultFor(filePath).Recommendation;

    private string ReasonFor(string filePath) => ResultFor(filePath).Reason;

    private FileScanResult ResultFor(string filePath) =>
        _store.ResultsOf(_reportId).Single(result => result.FilePath == filePath);

    private static FileScanResult FileOf(
        string filePath,
        long sizeBytes,
        DateTimeOffset? createdUtc = null,
        Recommendation recommendation = Recommendation.Retain,
        string reason = "In active use",
        IReadOnlyList<PiiFinding>? findings = null)
    {
        DateTimeOffset created = createdUtc ?? Now.AddDays(-1);

        return new FileScanResult(
            filePath,
            sizeBytes,
            created,
            created,
            created,
            recommendation,
            RiskLevel.None,
            reason,
            findings ?? []);
    }
}
