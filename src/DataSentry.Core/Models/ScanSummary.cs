namespace DataSentry.Core.Models;

/// <summary>
/// The headline the user sees first: "482 files, 3.1 GB reclaimable, 7 files need review".
/// Held on the report so the summary can be shown without loading every file row.
/// </summary>
public sealed record ScanSummary(
    int FilesScanned,
    long TotalSizeBytes,
    int FilesRecommendedForDeletion,
    long ReclaimableBytes,
    int FilesNeedingReview);
