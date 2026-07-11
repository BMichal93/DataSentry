using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Data.Persistence.Context;
using DataSentry.Data.Persistence.Mapping;
using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataSentry.Data.Persistence.Stores;

/// <summary>Stores scan reports in the local SQLite file.</summary>
public sealed class SqliteScanResultStore : IScanResultStore
{
    /// <summary>
    /// How many file rows are written per round trip. Large enough that a big scan is not a million
    /// tiny inserts, small enough that the change tracker never grows without bound.
    /// </summary>
    private const int InsertBatchSize = 500;

    private readonly IDbContextFactory<DataSentryDbContext> _contextFactory;

    public SqliteScanResultStore(IDbContextFactory<DataSentryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task SaveReportAsync(
        ScanReport report,
        IAsyncEnumerable<FileScanResult> results,
        CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        context.Reports.Add(ScanResultMapper.ToEntity(report));
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var batch = new List<FileScanResultEntity>(InsertBatchSize);

        await foreach (FileScanResult result in results.WithCancellation(cancellationToken))
        {
            batch.Add(ScanResultMapper.ToEntity(result, report.Id));

            if (batch.Count < InsertBatchSize)
            {
                continue;
            }

            await FlushAsync(context, batch, cancellationToken);
        }

        await FlushAsync(context, batch, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteReportAsync(ScanReport report, CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        ScanReportEntity? storedReport = await context.Reports
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(candidate => candidate.Id == report.Id, cancellationToken);

        if (storedReport is null)
        {
            throw new InvalidOperationException(
                $"Cannot complete report {report.Id}: it was never saved. This is a bug in the caller — " +
                "a report is saved when the scan starts and completed when it ends.");
        }

        ScanResultMapper.ApplyCompletion(storedReport, report);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScanReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        ScanReportEntity? report = await context.Reports
            .AsNoTracking()
            .Include(storedReport => storedReport.Errors)
            .SingleOrDefaultAsync(storedReport => storedReport.Id == reportId, cancellationToken);

        return report is null ? null : ScanResultMapper.ToDomain(report);
    }

    public async Task<IReadOnlyList<ScanReport>> ListReportsAsync(CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        List<ScanReportEntity> reports = await context.Reports
            .AsNoTracking()
            .Include(report => report.Errors)
            .OrderByDescending(report => report.CompletedUtc)
            .ToListAsync(cancellationToken);

        return reports.Select(ScanResultMapper.ToDomain).ToList();
    }

    public async IAsyncEnumerable<FileScanResult> GetResultsAsync(
        Guid reportId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IAsyncEnumerable<FileScanResultEntity> results = context.Results
            .AsNoTracking()
            .Include(result => result.Findings)
            .Where(result => result.ReportId == reportId)
            .OrderBy(result => result.Id)
            .AsAsyncEnumerable();

        await foreach (FileScanResultEntity result in results.WithCancellation(cancellationToken))
        {
            yield return ScanResultMapper.ToDomain(result);
        }
    }

    /// <remarks>
    /// SQLite does the skipping, and it does it on rows that are already indexed by report and ordered
    /// by insertion. What crosses back into the application is one page — a hundred rows — no matter how
    /// many million the scan wrote.
    /// </remarks>
    public async Task<IReadOnlyList<FileScanResult>> GetResultsPageAsync(
        Guid reportId,
        Recommendation recommendation,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        List<FileScanResultEntity> page = await context.Results
            .AsNoTracking()
            .Include(result => result.Findings)
            .Where(result => result.ReportId == reportId && result.Recommendation == recommendation)
            .OrderBy(result => result.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return page.Select(ScanResultMapper.ToDomain).ToList();
    }

    /// <remarks>
    /// The grouping happens in SQLite, not in the caller, and that is the point of it: the sizes are
    /// already indexed rows on disk, so the database can throw away every file that has no twin without
    /// a single path being read into memory. What comes back is only the candidates — on a real drive,
    /// a small fraction of the tree — ordered by size so each group arrives whole.
    /// </remarks>
    public async IAsyncEnumerable<DuplicateCandidate> GetDuplicateCandidatesAsync(
        Guid reportId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Empty files are excluded before anything else: they are all identical to one another, so they
        // would form the single largest group on any shared drive, and none of it would be worth
        // knowing — an empty file is already condemned by name alone.
        IQueryable<FileScanResultEntity> filesWithASize = context.Results
            .AsNoTracking()
            .Where(result => result.ReportId == reportId && result.SizeBytes > 0);

        IQueryable<long> sharedSizes = filesWithASize
            .GroupBy(result => result.SizeBytes)
            .Where(sameSizedFiles => sameSizedFiles.Count() > 1)
            .Select(sameSizedFiles => sameSizedFiles.Key);

        IAsyncEnumerable<DuplicateCandidate> candidates = filesWithASize
            .Where(result => sharedSizes.Contains(result.SizeBytes))
            .OrderBy(result => result.SizeBytes)
            .ThenBy(result => result.Id)
            .Select(result => new DuplicateCandidate(
                result.FilePath,
                result.SizeBytes,
                result.CreatedUtc,
                result.Recommendation,
                result.Findings.Count > 0))
            .AsAsyncEnumerable();

        await foreach (DuplicateCandidate candidate in candidates.WithCancellation(cancellationToken))
        {
            yield return candidate;
        }
    }

    public async Task ApplyDuplicateVerdictsAsync(
        Guid reportId,
        IReadOnlyList<DuplicateVerdict> verdicts,
        CancellationToken cancellationToken = default)
    {
        if (verdicts.Count == 0)
        {
            return;
        }

        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        Dictionary<string, DuplicateVerdict> verdictsByPath = verdicts.ToDictionary(
            verdict => verdict.FilePath,
            StringComparer.Ordinal);

        List<string> copiedPaths = [.. verdictsByPath.Keys];

        List<FileScanResultEntity> copies = await context.Results
            .Where(result => result.ReportId == reportId && copiedPaths.Contains(result.FilePath))
            .ToListAsync(cancellationToken);

        foreach (FileScanResultEntity copy in copies)
        {
            DuplicateVerdict verdict = verdictsByPath[copy.FilePath];

            copy.Recommendation = verdict.Recommendation;
            copy.Reason = verdict.Reason;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountPendingDeletionAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await PendingDeletion(context, reportId).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPathsPendingDeletionAsync(
        Guid reportId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await PendingDeletion(context, reportId)
            .OrderBy(result => result.Id)
            .Skip(skip)
            .Take(take)
            .Select(result => result.FilePath)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkRecycledAsync(
        Guid reportId,
        IReadOnlyList<string> filePaths,
        DateTimeOffset recycledUtc,
        CancellationToken cancellationToken = default)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // The verdict is in the WHERE clause, not merely in the caller's good intentions. Nothing that
        // was not condemned can be marked as deleted, however it got into this list.
        await PendingDeletion(context, reportId)
            .Where(result => filePaths.Contains(result.FilePath))
            .ExecuteUpdateAsync(
                result => result.SetProperty(recycled => recycled.RecycledUtc, recycledUtc),
                cancellationToken);
    }

    /// <summary>
    /// The files this report condemned and nobody has deleted yet — the one definition of "deletable",
    /// written once so that the count the user confirms and the paths that are actually recycled can
    /// never disagree about what it means.
    /// </summary>
    private static IQueryable<FileScanResultEntity> PendingDeletion(DataSentryDbContext context, Guid reportId) =>
        context.Results.Where(result =>
            result.ReportId == reportId &&
            result.Recommendation == Recommendation.Delete &&
            result.RecycledUtc == null);

    public async Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Reports
            .Where(report => report.Id == reportId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeReportsOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Reports
            .Where(report => report.CompletedUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task FlushAsync(
        DataSentryDbContext context,
        List<FileScanResultEntity> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        context.Results.AddRange(batch);
        await context.SaveChangesAsync(cancellationToken);

        context.ChangeTracker.Clear();
        batch.Clear();
    }
}
