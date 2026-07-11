using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// The store, without the database. Keeps the same two-step shape the real one has — the report is
/// saved when the scan starts and completed when it ends — so a test can tell the difference between
/// a scan that finished and one that was cancelled halfway.
/// </summary>
internal sealed class InMemoryScanResultStore : IScanResultStore
{
    private readonly Dictionary<Guid, ScanReport> _reports = [];
    private readonly Dictionary<Guid, List<FileScanResult>> _resultsByReport = [];

    /// <summary>The reports that were completed, as opposed to merely started.</summary>
    public List<ScanReport> CompletedReports { get; } = [];

    public IReadOnlyList<FileScanResult> ResultsOf(Guid reportId) =>
        _resultsByReport.TryGetValue(reportId, out List<FileScanResult>? results) ? results : [];

    public async Task SaveReportAsync(
        ScanReport report,
        IAsyncEnumerable<FileScanResult> results,
        CancellationToken cancellationToken = default)
    {
        _reports[report.Id] = report;

        var saved = new List<FileScanResult>();
        _resultsByReport[report.Id] = saved;

        await foreach (FileScanResult result in results.WithCancellation(cancellationToken))
        {
            saved.Add(result);
        }
    }

    public Task CompleteReportAsync(ScanReport report, CancellationToken cancellationToken = default)
    {
        _reports[report.Id] = report;
        CompletedReports.Add(report);

        return Task.CompletedTask;
    }

    public Task<ScanReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reports.GetValueOrDefault(reportId));

    public Task<IReadOnlyList<ScanReport>> ListReportsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScanReport>>(_reports.Values.OrderByDescending(report => report.CompletedUtc).ToList());

    public async IAsyncEnumerable<FileScanResult> GetResultsAsync(
        Guid reportId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (FileScanResult result in ResultsOf(reportId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return result;
        }

        await Task.CompletedTask;
    }

    public Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        _reports.Remove(reportId);
        _resultsByReport.Remove(reportId);

        return Task.CompletedTask;
    }

    public Task<int> PurgeReportsOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        List<Guid> expired = _reports.Values
            .Where(report => report.CompletedUtc < cutoffUtc)
            .Select(report => report.Id)
            .ToList();

        foreach (Guid reportId in expired)
        {
            _reports.Remove(reportId);
            _resultsByReport.Remove(reportId);
        }

        return Task.FromResult(expired.Count);
    }
}
