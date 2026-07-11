using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Data.Persistence.Context;
using DataSentry.Data.Persistence.Mapping;
using DataSentry.Data.Persistence.Models;
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
