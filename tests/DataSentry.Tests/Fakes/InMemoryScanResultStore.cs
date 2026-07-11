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

    /// <summary>
    /// The same page the SQLite store would return, and — the part that matters for a test — never more
    /// than <paramref name="take"/> of them, however many the scan wrote.
    /// </summary>
    public Task<IReadOnlyList<FileScanResult>> GetResultsPageAsync(
        Guid reportId,
        Recommendation recommendation,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FileScanResult> page = ResultsOf(reportId)
            .Where(result => result.Recommendation == recommendation)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult(page);
    }

    /// <summary>
    /// The same bargain the SQLite store strikes, kept by hand: only files that share a size with
    /// another, never an empty one, ordered so that each group of same-sized files arrives whole.
    /// </summary>
    public async IAsyncEnumerable<DuplicateCandidate> GetDuplicateCandidatesAsync(
        Guid reportId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<DuplicateCandidate> candidates = ResultsOf(reportId)
            .Where(result => result.SizeBytes > 0)
            .GroupBy(result => result.SizeBytes)
            .Where(sameSizedFiles => sameSizedFiles.Count() > 1)
            .OrderBy(sameSizedFiles => sameSizedFiles.Key)
            .SelectMany(sameSizedFiles => sameSizedFiles)
            .Select(result => new DuplicateCandidate(
                result.FilePath,
                result.SizeBytes,
                result.CreatedUtc,
                result.Recommendation,
                result.Findings.Count > 0))
            .ToList();

        foreach (DuplicateCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return candidate;
        }

        await Task.CompletedTask;
    }

    public Task ApplyDuplicateVerdictsAsync(
        Guid reportId,
        IReadOnlyList<DuplicateVerdict> verdicts,
        CancellationToken cancellationToken = default)
    {
        List<FileScanResult> results = _resultsByReport[reportId];

        foreach (DuplicateVerdict verdict in verdicts)
        {
            int index = results.FindIndex(result => result.FilePath == verdict.FilePath);

            results[index] = results[index] with
            {
                Recommendation = verdict.Recommendation,
                Reason = verdict.Reason
            };
        }

        return Task.CompletedTask;
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
