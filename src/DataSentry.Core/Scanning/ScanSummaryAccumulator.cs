using DataSentry.Core.Models;

namespace DataSentry.Core.Scanning;

/// <summary>
/// Adds the headline up as the results stream past — "482 files, 3.1 GB reclaimable, 7 files need
/// review". The results themselves are never held: a tree can hold millions of them, and five running
/// totals is all the summary ever needed from any of them.
/// </summary>
internal sealed class ScanSummaryAccumulator
{
    private int _filesScanned;
    private long _totalSizeBytes;
    private int _filesRecommendedForDeletion;
    private long _reclaimableBytes;
    private int _filesNeedingReview;

    public void Add(FileScanResult result)
    {
        _filesScanned++;
        _totalSizeBytes += result.SizeBytes;

        switch (result.Recommendation)
        {
            case Recommendation.Delete:
                _filesRecommendedForDeletion++;
                _reclaimableBytes += result.SizeBytes;
                break;

            case Recommendation.Review:
                _filesNeedingReview++;
                break;
        }
    }

    /// <summary>
    /// Corrects the headline for what the duplicate sweep changed after the fact. Every copy it
    /// condemned was counted here as a file to keep when it first went past — the sweep only ever turns
    /// a Retain into a Delete, so a file cannot be counted twice by it.
    /// </summary>
    public void AddDuplicatesMarkedForDeletion(DuplicateSweepResult duplicates)
    {
        _filesRecommendedForDeletion += duplicates.FilesMarkedForDeletion;
        _reclaimableBytes += duplicates.ReclaimableBytes;
    }

    public ScanSummary ToSummary() => new(
        _filesScanned,
        _totalSizeBytes,
        _filesRecommendedForDeletion,
        _reclaimableBytes,
        _filesNeedingReview);
}
