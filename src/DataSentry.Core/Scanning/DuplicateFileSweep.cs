using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Scanning;

/// <summary>
/// Finds the files that are copies of one another and condemns all but the original.
/// </summary>
/// <remarks>
/// <b>Why this is not an <see cref="Classification.IClassificationRule"/>.</b> Every other rule judges a
/// file on what that file alone can tell it: its name, its size, its age. This one cannot. Whether a
/// file is a duplicate is a fact about a *pair* of files, and the engine classifies each file as it
/// arrives and streams the verdict straight into storage — so at the moment a file is judged, the file
/// it copies may not have been walked yet. No amount of care inside <c>Evaluate</c> can answer a
/// question the abstraction is not given the information to answer.
///
/// So the duplicate verdict is settled afterwards, in a second pass over the results the scan has
/// already written down, and it is settled in three steps, each one cheaper than the step it spares:
///
/// <list type="number">
///   <item><b>Size.</b> Two files of different sizes are never copies. The store groups the results by
///   size on disk and hands back only the files that share one — which throws away almost every pair in
///   a real tree without a single file being opened.</item>
///   <item><b>Hash.</b> Only inside a group of two or more, because only there can the answer be yes.
///   Same size is not same content — two 4 KB invoices are not the same invoice — so the hash is what
///   actually decides, and it is the last question asked because reading a file to the end is the most
///   expensive thing this tool ever does.</item>
///   <item><b>Keeper.</b> One file in the set survives; the rest are copies.</item>
/// </list>
///
/// Nothing here holds more than one group of same-sized files at a time, which is what keeps the cost
/// proportional to the number of *candidates* rather than to the size of the tree. A scan of a shared
/// drive never buffers a million paths — the paths are on disk, where the scan just put them.
/// </remarks>
public sealed class DuplicateFileSweep
{
    private readonly IScanResultStore _resultStore;
    private readonly IFileContentReader _contentReader;

    public DuplicateFileSweep(IScanResultStore resultStore, IFileContentReader contentReader)
    {
        _resultStore = resultStore;
        _contentReader = contentReader;
    }

    /// <summary>
    /// Sweeps the report's results for copies and rewrites the verdict on every one it proves.
    /// </summary>
    /// <param name="reportError">
    /// Where a file that would not open goes. A copy that cannot be hashed is simply not proved to be a
    /// copy, and is left with the verdict it already had — one locked file must never take a scan down.
    /// </param>
    public async Task<DuplicateSweepResult> SweepAsync(
        Guid reportId,
        Action<ScanError> reportError,
        CancellationToken cancellationToken = default)
    {
        var sweep = new DuplicateSweepAccumulator();

        // The candidates arrive ordered by size, so a run of equal sizes is a group and the size
        // changing is the end of it. One group in memory, never the tree.
        var sameSizedFiles = new List<DuplicateCandidate>();
        long groupSizeBytes = -1;

        IAsyncEnumerable<DuplicateCandidate> candidates =
            _resultStore.GetDuplicateCandidatesAsync(reportId, cancellationToken);

        await foreach (DuplicateCandidate candidate in candidates.WithCancellation(cancellationToken))
        {
            if (candidate.SizeBytes != groupSizeBytes)
            {
                await ResolveGroupAsync(reportId, sameSizedFiles, sweep, reportError, cancellationToken);

                groupSizeBytes = candidate.SizeBytes;
                sameSizedFiles.Clear();
            }

            sameSizedFiles.Add(candidate);
        }

        await ResolveGroupAsync(reportId, sameSizedFiles, sweep, reportError, cancellationToken);

        return sweep.ToResult();
    }

    /// <summary>
    /// Takes one group of same-sized files and works out which of them are actually the same file.
    /// This is the only place in DataSentry that reads a file to the end, and it is reached only when
    /// a cheaper signal has already said it might be worth it.
    /// </summary>
    private async Task ResolveGroupAsync(
        Guid reportId,
        IReadOnlyList<DuplicateCandidate> sameSizedFiles,
        DuplicateSweepAccumulator sweep,
        Action<ScanError> reportError,
        CancellationToken cancellationToken)
    {
        // A group of one is not a group. The store should never hand one over, but hashing a file that
        // has nothing to be compared against is exactly the wasted read this whole design exists to
        // avoid, so the guard stays.
        if (sameSizedFiles.Count < 2)
        {
            return;
        }

        Dictionary<string, List<DuplicateCandidate>> filesByContent =
            await GroupByContentAsync(sameSizedFiles, reportError, cancellationToken);

        var verdicts = new List<DuplicateVerdict>();

        foreach (List<DuplicateCandidate> identicalFiles in filesByContent.Values)
        {
            // Same size, different content: two files that were only ever coincidentally alike. The
            // hash is what decides, and here it has said no.
            if (identicalFiles.Count < 2)
            {
                continue;
            }

            verdicts.AddRange(CondemnCopies(identicalFiles, sweep));
        }

        await _resultStore.ApplyDuplicateVerdictsAsync(reportId, verdicts, cancellationToken);
    }

    private async Task<Dictionary<string, List<DuplicateCandidate>>> GroupByContentAsync(
        IReadOnlyList<DuplicateCandidate> sameSizedFiles,
        Action<ScanError> reportError,
        CancellationToken cancellationToken)
    {
        var filesByContent = new Dictionary<string, List<DuplicateCandidate>>(StringComparer.Ordinal);

        foreach (DuplicateCandidate candidate in sameSizedFiles)
        {
            string contentHash;

            try
            {
                contentHash = await _contentReader.ComputeContentHashAsync(candidate.FilePath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception unreadable)
            {
                // Denied, locked, or deleted since it was walked. Unproven is not the same as innocent,
                // but it is the only safe reading of it: the file keeps the verdict it already had.
                reportError(new ScanError(candidate.FilePath, unreadable.Message));

                continue;
            }

            if (!filesByContent.TryGetValue(contentHash, out List<DuplicateCandidate>? identicalFiles))
            {
                identicalFiles = [];
                filesByContent[contentHash] = identicalFiles;
            }

            identicalFiles.Add(candidate);
        }

        return filesByContent;
    }

    /// <summary>
    /// One file out of a set of identical ones is the original; the others are the copies.
    /// </summary>
    /// <remarks>
    /// The keeper is <b>the oldest</b> — the file that existed before the others did, which is the one a
    /// user would point at if asked which of them is the real one. The copies are what somebody made
    /// later: dragged into a "backup" folder, saved again as "report (2).xlsx", mailed to themselves.
    /// The alternative — trusting the folder a file sits in, or a "copy" in its name — reads well until
    /// the day somebody's original genuinely lives in a folder called "Copy of 2023" and the tool
    /// recommends deleting the only one there is. Creation time is a fact; a folder name is a guess.
    ///
    /// Ties are broken by path, so that two files created in the same tick — a folder copied wholesale
    /// gives you plenty — settle it the same way on every run rather than on whatever order the disk
    /// happened to hand them over in. A recommendation that changes between two scans of an unchanged
    /// drive is one nobody can trust.
    /// </remarks>
    private static IEnumerable<DuplicateVerdict> CondemnCopies(
        List<DuplicateCandidate> identicalFiles,
        DuplicateSweepAccumulator sweep)
    {
        DuplicateCandidate keeper = identicalFiles
            .OrderBy(file => file.CreatedUtc)
            .ThenBy(file => file.FilePath, StringComparer.Ordinal)
            .First();

        foreach (DuplicateCandidate copy in identicalFiles)
        {
            if (ReferenceEquals(copy, keeper) || !IsDeletable(copy))
            {
                continue;
            }

            sweep.MarkedForDeletion(copy.SizeBytes);

            yield return new DuplicateVerdict(
                copy.FilePath,
                Recommendation.Delete,
                $"Identical copy of {Path.GetFileName(keeper.FilePath)}, which is kept");
        }
    }

    /// <summary>
    /// Whether the sweep is allowed to condemn this copy at all.
    /// </summary>
    /// <remarks>
    /// A copy holding personal data is not deleted, and being a copy changes nothing about that. The
    /// duplicate of a payroll spreadsheet is a payroll spreadsheet: it may be under a retention
    /// obligation, and the tool that quietly deleted one because an identical one existed elsewhere
    /// would have deleted personal data unasked. It is surfaced instead — the same rule that stops a
    /// stale file or a temporary file from being deleted when a detector finds something in it, and it
    /// does not bend for this rule any more than it bends for those.
    ///
    /// A file already condemned keeps the reason it was condemned for. "Temporary file" is a better
    /// answer than "Identical copy of…", and it was true first.
    /// </remarks>
    private static bool IsDeletable(DuplicateCandidate copy) =>
        !copy.HoldsPersonalData && copy.Recommendation is not (Recommendation.Review or Recommendation.Delete);

    /// <summary>The two running totals the summary has to be corrected by. Never the copies themselves.</summary>
    private sealed class DuplicateSweepAccumulator
    {
        private int _filesMarkedForDeletion;
        private long _reclaimableBytes;

        public void MarkedForDeletion(long sizeBytes)
        {
            _filesMarkedForDeletion++;
            _reclaimableBytes += sizeBytes;
        }

        public DuplicateSweepResult ToResult() => new(_filesMarkedForDeletion, _reclaimableBytes);
    }
}
