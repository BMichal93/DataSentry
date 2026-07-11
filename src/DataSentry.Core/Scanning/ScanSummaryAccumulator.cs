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

    public ScanSummary ToSummary() => new(
        _filesScanned,
        _totalSizeBytes,
        _filesRecommendedForDeletion,
        _reclaimableBytes,
        _filesNeedingReview);
}
