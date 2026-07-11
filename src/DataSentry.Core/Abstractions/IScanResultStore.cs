using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Models;

namespace DataSentry.Core.Abstractions;

/// <summary>
/// Where scan reports live. Core defines the contract and knows nothing about how — or whether —
/// they are written to a disk.
/// </summary>
public interface IScanResultStore
{
    /// <summary>
    /// Persists a report and its per-file results. Results are streamed rather than handed over as a
    /// list, so a scan of a million files never has to sit in memory at once.
    /// </summary>
    Task SaveReportAsync(
        ScanReport report,
        IAsyncEnumerable<FileScanResult> results,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the outcome onto a report whose results have already been streamed in: when it finished,
    /// what it added up to, and everything it could not read.
    /// </summary>
    /// <remarks>
    /// This arrives second because none of it exists first. The summary is the sum of the results and
    /// the errors are whatever the walk tripped over, so both are only known once the last file has
    /// gone through — and by then the results have long since been streamed past, precisely so that
    /// they never had to be held in memory to be counted.
    /// </remarks>
    Task CompleteReportAsync(ScanReport report, CancellationToken cancellationToken = default);

    /// <summary>The report metadata and summary, without the file rows. Null if it is not there.</summary>
    Task<ScanReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>Every stored report, newest scan first.</summary>
    Task<IReadOnlyList<ScanReport>> ListReportsAsync(CancellationToken cancellationToken = default);

    /// <summary>The per-file results of a report, streamed for display.</summary>
    IAsyncEnumerable<FileScanResult> GetResultsAsync(Guid reportId, CancellationToken cancellationToken = default);

    Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every report scanned before <paramref name="cutoffUtc"/>, and returns how many went.
    /// Deletes the row — it does not flag it.
    /// </summary>
    Task<int> PurgeReportsOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
