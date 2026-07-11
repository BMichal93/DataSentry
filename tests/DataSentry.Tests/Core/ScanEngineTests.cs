using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Tests.Fakes;

namespace DataSentry.Tests.Core;

[TestFixture]
public class ScanEngineTests
{
    private const string ValidIban = "PL61 1090 1014 0000 0712 1981 2874";

    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    private InMemoryScanResultStore _store = null!;

    [SetUp]
    public void SetUp() => _store = new InMemoryScanResultStore();

    [Test]
    public async Task ScanAsync_DirectoryOfOrdinaryFiles_ClassifiesEveryOneOfThem()
    {
        ScanReport report = await ScanAsync(
            [FileAt("C:/work/report.docx"), FileAt("C:/work/export.tmp"), FileAt("C:/work/old.xlsx", Now.AddYears(-4))]);

        IReadOnlyList<FileScanResult> results = _store.ResultsOf(report.Id);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(RecommendationFor(results, "C:/work/report.docx"), Is.EqualTo(Recommendation.Retain));
            Assert.That(RecommendationFor(results, "C:/work/export.tmp"), Is.EqualTo(Recommendation.Delete));
            Assert.That(RecommendationFor(results, "C:/work/old.xlsx"), Is.EqualTo(Recommendation.Delete));
        });
    }

    [Test]
    public async Task ScanAsync_StaleFileHoldingAnAccountNumber_IsSurfacedForReviewRatherThanDeleted()
    {
        ScanReport report = await ScanAsync(
            [FileAt("C:/work/suppliers-2021.csv", Now.AddYears(-4))],
            textByPath: new Dictionary<string, string?> { ["C:/work/suppliers-2021.csv"] = $"Kowalski,{ValidIban}" });

        FileScanResult result = _store.ResultsOf(report.Id).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Recommendation, Is.EqualTo(Recommendation.Review));
            Assert.That(result.RiskLevel, Is.EqualTo(RiskLevel.High));
            Assert.That(result.Findings.Single().MatchCount, Is.EqualTo(1));
            Assert.That(result.Reason, Does.Contain("1 IBAN"));
        });
    }

    [Test]
    public async Task ScanAsync_FindingInAFile_NeverCarriesTheMatchedValue()
    {
        ScanReport report = await ScanAsync(
            [FileAt("C:/work/suppliers.csv")],
            textByPath: new Dictionary<string, string?> { ["C:/work/suppliers.csv"] = $"Kowalski,{ValidIban}" });

        FileScanResult result = _store.ResultsOf(report.Id).Single();

        Assert.That(
            result.Reason,
            Does.Not.Contain("2874").And.Not.Contain("PL61"),
            "the tool reports what it found, never what it read");
    }

    [Test]
    public async Task ScanAsync_FileLockedByAnotherProcess_RecordsAnErrorAndKeepsScanning()
    {
        ScanReport report = await ScanAsync(
            [FileAt("C:/work/open-in-excel.xlsx"), FileAt("C:/work/report.docx")],
            unreadablePaths: new HashSet<string> { "C:/work/open-in-excel.xlsx" });

        Assert.Multiple(() =>
        {
            Assert.That(_store.ResultsOf(report.Id), Has.Count.EqualTo(2), "the locked file is still judged on its name and its age");
            Assert.That(report.Errors.Single().Path, Is.EqualTo("C:/work/open-in-excel.xlsx"));
        });
    }

    [Test]
    public async Task ScanAsync_FolderTheWalkerCouldNotOpen_IsReportedOnTheReport()
    {
        var deniedFolder = new ScanError("C:/work/private", "Access to the path 'C:/work/private' is denied.");

        ScanReport report = await ScanAsync([FileAt("C:/work/report.docx")], walkErrors: [deniedFolder]);

        Assert.Multiple(() =>
        {
            Assert.That(report.Errors, Is.EqualTo(new[] { deniedFolder }));
            Assert.That(_store.ResultsOf(report.Id), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ScanAsync_ScanOfMixedFiles_AddsUpTheHeadline()
    {
        ScanReport report = await ScanAsync(
        [
            FileAt("C:/work/report.docx", sizeBytes: 1_000),
            FileAt("C:/work/export.tmp", sizeBytes: 2_000),
            FileAt("C:/work/backup.bak", sizeBytes: 3_000),
            FileAt("C:/work/suppliers.csv", sizeBytes: 4_000)
        ],
        textByPath: new Dictionary<string, string?> { ["C:/work/suppliers.csv"] = $"Kowalski,{ValidIban}" });

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.FilesScanned, Is.EqualTo(4));
            Assert.That(report.Summary.TotalSizeBytes, Is.EqualTo(10_000));
            Assert.That(report.Summary.FilesRecommendedForDeletion, Is.EqualTo(2));
            Assert.That(report.Summary.ReclaimableBytes, Is.EqualTo(5_000));
            Assert.That(report.Summary.FilesNeedingReview, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ScanAsync_WhileItRuns_ReportsWhatItHasFoundAndWhatItHasGotThrough()
    {
        var reported = new ConcurrentBag<ScanProgress>();

        await ScanAsync(
            [FileAt("C:/work/a.docx"), FileAt("C:/work/b.docx"), FileAt("C:/work/c.docx")],
            progress: new Progress<ScanProgress>(reported.Add));

        // Progress<T> hands each report off to the thread pool, so they arrive when they arrive — the
        // engine counts every file exactly once, and the order they land in is the UI's business.
        Assert.That(
            () => reported.Select(progress => progress.FilesScanned).OrderBy(scanned => scanned),
            Is.EqualTo(new[] { 1, 2, 3 }).After(1000, 20));

        Assert.That(
            reported.Select(progress => progress.CurrentFilePath),
            Is.EquivalentTo(new[] { "C:/work/a.docx", "C:/work/b.docx", "C:/work/c.docx" }));
    }

    [Test]
    public void ScanAsync_CancelledMidScan_StopsCleanlyAndCompletesNoReport()
    {
        using var cancellation = new CancellationTokenSource();

        // The walk is what notices: the third file is never handed to the classifier.
        var files = new List<FileMetadata>
        {
            FileAt("C:/work/a.docx"),
            FileAt("C:/work/b.docx"),
            FileAt("C:/work/c.docx")
        };

        var engine = new ScanEngine(
            new FakeFileSource(files, onFileEnumerated: file =>
            {
                if (file.FilePath.EndsWith("b.docx", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            }),
            new FakeFileContentReader(),
            _store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new FixedTimeProvider(Now));

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await engine.ScanAsync(new ScanScope("C:/work"), progress: null, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(_store.CompletedReports, Is.Empty, "a scan that was stopped halfway has no report to show");
        });
    }

    private async Task<ScanReport> ScanAsync(
        IReadOnlyList<FileMetadata> files,
        IReadOnlyDictionary<string, string?>? textByPath = null,
        IReadOnlySet<string>? unreadablePaths = null,
        IReadOnlyList<ScanError>? walkErrors = null,
        IProgress<ScanProgress>? progress = null)
    {
        var engine = new ScanEngine(
            new FakeFileSource(files, walkErrors),
            new FakeFileContentReader(textByPath, unreadablePaths),
            _store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new FixedTimeProvider(Now));

        return await engine.ScanAsync(new ScanScope("C:/work"), progress);
    }

    private static Recommendation RecommendationFor(IReadOnlyList<FileScanResult> results, string filePath) =>
        results.Single(result => result.FilePath == filePath).Recommendation;

    private static FileMetadata FileAt(string filePath, DateTimeOffset? lastTouchedUtc = null, long sizeBytes = 4_096)
    {
        DateTimeOffset touched = lastTouchedUtc ?? Now.AddDays(-1);

        return new FileMetadata(filePath, sizeBytes, touched, touched, touched);
    }
}
