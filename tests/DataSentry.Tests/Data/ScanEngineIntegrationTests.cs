using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Data;
using DataSentry.Data.Persistence.Context;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.Tests.Data;

/// <summary>
/// The whole thing, end to end: a real directory on a real disk, walked by the real walker, read by
/// the real extractors, stored in a real SQLite file. Everything the fakes in the Core tests stand in
/// for, doing what it actually does.
/// </summary>
[TestFixture]
public class ScanEngineIntegrationTests
{
    private const string ValidIban = "PL61 1090 1014 0000 0712 1981 2874";
    private const string ValidCard = "4111 1111 1111 1111";
    private const string ValidPesel = "90031500015";

    private string _scanRoot = string.Empty;
    private string _databasePath = string.Empty;
    private ServiceProvider _services = null!;
    private IScanResultStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scanRoot = Path.Combine(Path.GetTempPath(), $"datasentry-scan-{Guid.NewGuid():N}");
        _databasePath = Path.Combine(Path.GetTempPath(), $"datasentry-scan-{Guid.NewGuid():N}.db");

        Directory.CreateDirectory(_scanRoot);

        _services = new ServiceCollection()
            .AddDataSentryPersistence(_databasePath)
            .AddDataSentryFileSystem()
            .BuildServiceProvider();

        await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        _store = _services.GetRequiredService<IScanResultStore>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();

        SqliteConnection.ClearAllPools();

        File.Delete(_databasePath);
        Directory.Delete(_scanRoot, recursive: true);
    }

    [Test]
    public async Task ScanAsync_RealDirectoryTree_ClassifiesItAndStoresTheReport()
    {
        WriteFile("q3-notes.txt", "Nothing in here identifies anybody.");
        WriteFile("build.tmp", "leftovers");
        WriteFile(Path.Combine("archive", "suppliers.csv"), $"Kowalski,{ValidIban}");

        ScanReport report = await BuildEngine().ScanAsync(new ScanScope(_scanRoot));

        List<FileScanResult> results = await _store.GetResultsAsync(report.Id).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(report.Summary.FilesScanned, Is.EqualTo(3));
            Assert.That(report.Summary.FilesNeedingReview, Is.EqualTo(1));
            Assert.That(report.Summary.FilesRecommendedForDeletion, Is.EqualTo(1));
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(RecommendationFor(results, "q3-notes.txt"), Is.EqualTo(Recommendation.Retain));
            Assert.That(RecommendationFor(results, "build.tmp"), Is.EqualTo(Recommendation.Delete));
            Assert.That(RecommendationFor(results, "suppliers.csv"), Is.EqualTo(Recommendation.Review));
        });
    }

    [Test]
    public async Task ScanAsync_AccountNumberInASpreadsheet_IsStoredAsATypeAndACountAndNothingElse()
    {
        WriteFile("suppliers.csv", $"Supplier,Account\nKowalski,{ValidIban}");

        ScanReport report = await BuildEngine().ScanAsync(new ScanScope(_scanRoot));

        FileScanResult result = (await _store.GetResultsAsync(report.Id).ToListAsync()).Single();
        PiiFinding finding = result.Findings.Single();

        Assert.Multiple(() =>
        {
            Assert.That(finding.Category, Is.EqualTo(PiiCategory.Financial));
            Assert.That(finding.DetectorName, Is.EqualTo("IBAN"));
            Assert.That(finding.MatchCount, Is.EqualTo(1));
        });

        // Read the database as the bytes it is, not through the model that wrote it: the question is
        // whether the account number is anywhere on that disk, and the model is the last place that
        // would admit it.
        SqliteConnection.ClearAllPools();
        string everythingWeWroteDown = await File.ReadAllTextAsync(_databasePath);

        Assert.That(
            everythingWeWroteDown,
            Does.Not.Contain("2874").And.Not.Contain("PL61109010140000071219812874"),
            "the database must never become a copy of the data it was built to police");
    }

    [Test]
    public async Task ScanAsync_CardNumbersAndIdentityNumbersInASpreadsheet_AreStoredAsTypesAndCountsAndNothingElse()
    {
        WriteFile(
            "payroll.csv",
            $"""
             Name,PESEL,Card
             Kowalski,{ValidPesel},{ValidCard}
             """);

        ScanReport report = await BuildEngine().ScanAsync(new ScanScope(_scanRoot));

        FileScanResult result = (await _store.GetResultsAsync(report.Id).ToListAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Findings.Select(finding => finding.Category),
                Is.EquivalentTo(new[] { PiiCategory.Financial, PiiCategory.Identity }));
            Assert.That(result.Findings.Select(finding => finding.MatchCount), Is.All.EqualTo(1));
        });

        // Read the database as the bytes it is, not through the model that wrote it: the question is
        // whether the card number and the identity number are anywhere on that disk, and the model is
        // the last place that would admit it.
        SqliteConnection.ClearAllPools();
        string everythingWeWroteDown = await File.ReadAllTextAsync(_databasePath);

        Assert.Multiple(() =>
        {
            Assert.That(
                everythingWeWroteDown,
                Does.Not.Contain(ValidCard).And.Not.Contain("1111 1111"),
                "the database must never become a copy of the data it was built to police");
            Assert.That(
                everythingWeWroteDown,
                Does.Not.Contain(ValidPesel).And.Not.Contain("0031500"),
                "and that goes for the identity number too");
        });
    }

    [Test]
    public async Task ScanAsync_FileHeldOpenByAnotherProcess_IsRecordedAsAnErrorAndTheScanFinishes()
    {
        WriteFile("q3-notes.txt", "Nothing in here identifies anybody.");
        WriteFile("locked.csv", $"Kowalski,{ValidIban}");

        string lockedFilePath = Path.Combine(_scanRoot, "locked.csv");

        using (new FileStream(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ScanReport report = await BuildEngine().ScanAsync(new ScanScope(_scanRoot));

            Assert.Multiple(() =>
            {
                Assert.That(report.Summary.FilesScanned, Is.EqualTo(2), "a file nobody can open is still a file");
                Assert.That(report.Errors.Single().Path, Is.EqualTo(lockedFilePath));
            });
        }
    }

    [Test]
    public async Task ScanAsync_CancelledMidScan_StopsCleanlyAndLeavesNoReportBehind()
    {
        foreach (int fileNumber in Enumerable.Range(1, 50))
        {
            WriteFile($"notes-{fileNumber}.txt", "Nothing in here identifies anybody.");
        }

        using var cancellation = new CancellationTokenSource();

        // Cancelled once the scan is properly under way, rather than after a race with a progress
        // callback: the user hits Cancel while a file is being read, and this is that moment.
        var contentReader = new CancellingFileContentReader(
            _services.GetRequiredService<IFileContentReader>(),
            cancellation,
            cancelAfterFiles: 5);

        Assert.That(
            async () => await BuildEngine(contentReader).ScanAsync(new ScanScope(_scanRoot), progress: null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());

        IReadOnlyList<ScanReport> reports = await _store.ListReportsAsync();

        Assert.That(reports, Is.Empty, "a scan that was stopped halfway rolls back: there is nothing to act on");
    }

    private ScanEngine BuildEngine(IFileContentReader? contentReader = null) => new(
        _services.GetRequiredService<IFileSource>(),
        contentReader ?? _services.GetRequiredService<IFileContentReader>(),
        _store,
        [new JunkFileRule(), new StaleFileRule()],
        [
            new SpecialCategoryDetector(),
            new IbanDetector(),
            new PaymentCardDetector(),
            new PeselDetector(),
            new EmailAddressDetector(),
            new PhoneNumberDetector(),
            new IpAddressDetector()
        ],
        TimeProvider.System);

    /// <summary>The real reader, with a hand on the Cancel button.</summary>
    private sealed class CancellingFileContentReader(
        IFileContentReader contentReader,
        CancellationTokenSource cancellation,
        int cancelAfterFiles) : IFileContentReader
    {
        private int _filesRead;

        public Task<string?> ReadTextSampleAsync(string filePath, int maxCharacters, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _filesRead) == cancelAfterFiles)
            {
                cancellation.Cancel();
            }

            return contentReader.ReadTextSampleAsync(filePath, maxCharacters, cancellationToken);
        }

        public Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken = default) =>
            contentReader.ComputeContentHashAsync(filePath, cancellationToken);
    }

    private void WriteFile(string relativePath, string content)
    {
        string filePath = Path.Combine(_scanRoot, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }

    private static Recommendation RecommendationFor(IEnumerable<FileScanResult> results, string fileName) =>
        results.Single(result => Path.GetFileName(result.FilePath) == fileName).Recommendation;
}
