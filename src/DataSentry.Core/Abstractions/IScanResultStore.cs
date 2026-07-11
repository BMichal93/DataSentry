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

    /// <summary>
    /// Every file in the report that shares its size with at least one other file, ordered by size so
    /// that each group of same-sized files arrives in one run. Files of a size no other file shares are
    /// never returned: two files of different sizes cannot be copies of each other, and that one fact
    /// eliminates almost every pair in a real tree without anything being opened.
    /// </summary>
    /// <remarks>
    /// <b>The grouping is the store's job, and this is the whole reason why.</b> The store has already
    /// written every path and size to disk, and a database sorting rows on disk is what a database is
    /// for — so the candidates can be found without a single path being held in memory. The caller sees
    /// only files that already have a twin, which is what keeps the cost of a duplicate sweep
    /// proportional to the number of candidates rather than to the size of the tree.
    ///
    /// Empty files are never candidates. They are all byte-for-byte identical to one another, so a
    /// shared drive can hold thousands in one group — and there is nothing to gain by hashing them,
    /// since an empty file is already condemned by name alone ("Empty file") and holds nothing that
    /// deleting it could lose.
    /// </remarks>
    IAsyncEnumerable<DuplicateCandidate> GetDuplicateCandidatesAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the sweep's rulings over the verdicts those files were first given. Applied one group of
    /// copies at a time, so that a scan which found a great many of them never gathers them all up first.
    /// </summary>
    Task ApplyDuplicateVerdictsAsync(
        Guid reportId,
        IReadOnlyList<DuplicateVerdict> verdicts,
        CancellationToken cancellationToken = default);

    Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every report scanned before <paramref name="cutoffUtc"/>, and returns how many went.
    /// Deletes the row — it does not flag it.
    /// </summary>
    Task<int> PurgeReportsOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
