using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Data;
using DataSentry.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.Tests.Data;

[TestFixture]
public class SqliteScanResultStoreTests
{
    private string _databasePath = string.Empty;
    private ServiceProvider _services = null!;
    private IScanResultStore _store = null!;

    [SetUp]
    public async Task SetUp()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"datasentry-tests-{Guid.NewGuid():N}.db");

        _services = new ServiceCollection()
            .AddDataSentryPersistence(_databasePath)
            .BuildServiceProvider();

        await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        _store = _services.GetRequiredService<IScanResultStore>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();

        // Disposing the context does not close the pooled connection, and Windows will not delete a
        // file that still has an open handle.
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    [Test]
    public async Task SaveReport_ReportWithResults_RoundTripsEveryFileAndFinding()
    {
        ScanReport report = CreateReport(Guid.NewGuid(), completedUtc: DateTimeOffset.UtcNow);
        FileScanResult staleInvoice = CreateResult(
            @"C:\shared\invoices\2019.xlsx",
            Recommendation.Review,
            new PiiFinding(PiiCategory.Financial, "Iban", MatchCount: 3, Confidence: 0.95));

        await _store.SaveReportAsync(report, ToAsyncEnumerable(staleInvoice, CreateResult(@"C:\temp\a.tmp", Recommendation.Delete)));

        List<FileScanResult> storedResults = await _store.GetResultsAsync(report.Id).ToListAsync();
        Assert.That(storedResults, Has.Count.EqualTo(2));

        FileScanResult storedInvoice = storedResults.Single(result => result.FilePath == staleInvoice.FilePath);
        Assert.Multiple(() =>
        {
            Assert.That(storedInvoice.Recommendation, Is.EqualTo(Recommendation.Review));
            Assert.That(storedInvoice.Findings.Single().Category, Is.EqualTo(PiiCategory.Financial));
            Assert.That(storedInvoice.Findings.Single().MatchCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task SaveReport_MoreResultsThanOneInsertBatch_StoresAllOfThem()
    {
        ScanReport report = CreateReport(Guid.NewGuid(), completedUtc: DateTimeOffset.UtcNow);
        IEnumerable<FileScanResult> manyResults = Enumerable
            .Range(0, 1201)
            .Select(index => CreateResult($@"C:\shared\file-{index}.txt", Recommendation.Retain));

        await _store.SaveReportAsync(report, ToAsyncEnumerable(manyResults.ToArray()));

        List<FileScanResult> storedResults = await _store.GetResultsAsync(report.Id).ToListAsync();
        Assert.That(storedResults, Has.Count.EqualTo(1201));
    }

    [Test]
    public async Task GetReport_SavedReport_ReturnsSummaryAndErrorsWithoutFileRows()
    {
        ScanReport report = CreateReport(Guid.NewGuid(), completedUtc: DateTimeOffset.UtcNow);

        await _store.SaveReportAsync(report, ToAsyncEnumerable(CreateResult(@"C:\a.txt", Recommendation.Delete)));

        ScanReport? storedReport = await _store.GetReportAsync(report.Id);
        Assert.That(storedReport, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(storedReport!.Summary.FilesScanned, Is.EqualTo(report.Summary.FilesScanned));
            Assert.That(storedReport.RootPath, Is.EqualTo(report.RootPath));
            Assert.That(storedReport.Errors.Single().Path, Is.EqualTo(@"C:\shared\locked.docx"));
        });
    }

    [Test]
    public async Task GetReport_UnknownReportId_ReturnsNull()
    {
        Assert.That(await _store.GetReportAsync(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task DeleteReport_ReportWithResults_RemovesTheResultsToo()
    {
        ScanReport report = CreateReport(Guid.NewGuid(), completedUtc: DateTimeOffset.UtcNow);
        await _store.SaveReportAsync(report, ToAsyncEnumerable(CreateResult(@"C:\a.txt", Recommendation.Delete)));

        await _store.DeleteReportAsync(report.Id);

        Assert.That(await _store.GetReportAsync(report.Id), Is.Null);
        Assert.That(await _store.GetResultsAsync(report.Id).ToListAsync(), Is.Empty);
    }

    [Test]
    public async Task PurgeReportsOlderThan_ReportScannedBeforeTheCutoff_DeletesItAndKeepsTheRecentOne()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ScanReport expiredReport = CreateReport(Guid.NewGuid(), completedUtc: now.AddDays(-31));
        ScanReport recentReport = CreateReport(Guid.NewGuid(), completedUtc: now.AddDays(-29));

        await _store.SaveReportAsync(expiredReport, ToAsyncEnumerable(CreateResult(@"C:\old.txt", Recommendation.Delete)));
        await _store.SaveReportAsync(recentReport, ToAsyncEnumerable(CreateResult(@"C:\new.txt", Recommendation.Delete)));

        int purgedReports = await _store.PurgeReportsOlderThanAsync(now.AddDays(-30));

        Assert.That(purgedReports, Is.EqualTo(1));
        Assert.That(await _store.GetReportAsync(expiredReport.Id), Is.Null);
        Assert.That(await _store.GetResultsAsync(expiredReport.Id).ToListAsync(), Is.Empty);
        Assert.That(await _store.GetReportAsync(recentReport.Id), Is.Not.Null);
    }

    [Test]
    public async Task ListReports_SeveralReports_ReturnsNewestScanFirst()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ScanReport olderReport = CreateReport(Guid.NewGuid(), completedUtc: now.AddDays(-2));
        ScanReport newerReport = CreateReport(Guid.NewGuid(), completedUtc: now);

        await _store.SaveReportAsync(olderReport, ToAsyncEnumerable());
        await _store.SaveReportAsync(newerReport, ToAsyncEnumerable());

        IReadOnlyList<ScanReport> storedReports = await _store.ListReportsAsync();

        Assert.That(storedReports.Select(report => report.Id), Is.EqualTo(new[] { newerReport.Id, olderReport.Id }));
    }

    private static ScanReport CreateReport(Guid id, DateTimeOffset completedUtc) => new(
        id,
        @"C:\shared",
        completedUtc.AddMinutes(-5),
        completedUtc,
        new ScanSummary(
            FilesScanned: 482,
            TotalSizeBytes: 9_000_000_000,
            FilesRecommendedForDeletion: 120,
            ReclaimableBytes: 3_100_000_000,
            FilesNeedingReview: 7),
        [new ScanError(@"C:\shared\locked.docx", "The file is in use by another process.")]);

    private static FileScanResult CreateResult(
        string filePath,
        Recommendation recommendation,
        params PiiFinding[] findings) => new(
        filePath,
        SizeBytes: 1024,
        CreatedUtc: DateTimeOffset.UtcNow.AddYears(-4),
        LastModifiedUtc: DateTimeOffset.UtcNow.AddYears(-3),
        LastAccessedUtc: DateTimeOffset.UtcNow.AddYears(-3),
        recommendation,
        RiskLevel.Medium,
        Reason: "Not opened in 3 years",
        findings);

    private static async IAsyncEnumerable<FileScanResult> ToAsyncEnumerable(params FileScanResult[] results)
    {
        foreach (FileScanResult result in results)
        {
            yield return result;
        }

        await Task.CompletedTask;
    }
}
