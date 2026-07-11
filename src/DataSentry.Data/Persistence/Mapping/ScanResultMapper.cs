using System.Linq;
using DataSentry.Core.Models;
using DataSentry.Data.Persistence.Entities;

namespace DataSentry.Data.Persistence.Mapping;

/// <summary>Translates between the Core domain records and the persistence models, in both directions.</summary>
internal static class ScanResultMapper
{
    public static ScanReportEntity ToEntity(ScanReport report) => new()
    {
        Id = report.Id,
        RootPath = report.RootPath,
        StartedUtc = report.StartedUtc,
        CompletedUtc = report.CompletedUtc,
        FilesScanned = report.Summary.FilesScanned,
        TotalSizeBytes = report.Summary.TotalSizeBytes,
        FilesRecommendedForDeletion = report.Summary.FilesRecommendedForDeletion,
        ReclaimableBytes = report.Summary.ReclaimableBytes,
        FilesNeedingReview = report.Summary.FilesNeedingReview,
        Errors = report.Errors.Select(error => ToEntity(error, report.Id)).ToList()
    };

    public static FileScanResultEntity ToEntity(FileScanResult result, Guid reportId) => new()
    {
        ReportId = reportId,
        FilePath = result.FilePath,
        SizeBytes = result.SizeBytes,
        CreatedUtc = result.CreatedUtc,
        LastModifiedUtc = result.LastModifiedUtc,
        LastAccessedUtc = result.LastAccessedUtc,
        Recommendation = result.Recommendation,
        RiskLevel = result.RiskLevel,
        Reason = result.Reason,
        RecycledUtc = result.RecycledUtc,
        Findings = result.Findings.Select(ToEntity).ToList()
    };

    /// <summary>
    /// Writes the outcome of a finished scan onto the row that was written when it started. The errors
    /// are replaced rather than appended: this says how the scan ended, and it says it once.
    /// </summary>
    public static void ApplyCompletion(ScanReportEntity entity, ScanReport report)
    {
        entity.CompletedUtc = report.CompletedUtc;
        entity.FilesScanned = report.Summary.FilesScanned;
        entity.TotalSizeBytes = report.Summary.TotalSizeBytes;
        entity.FilesRecommendedForDeletion = report.Summary.FilesRecommendedForDeletion;
        entity.ReclaimableBytes = report.Summary.ReclaimableBytes;
        entity.FilesNeedingReview = report.Summary.FilesNeedingReview;

        entity.Errors.Clear();
        entity.Errors.AddRange(report.Errors.Select(error => ToEntity(error, report.Id)));
    }

    public static ScanReport ToDomain(ScanReportEntity entity) => new(
        entity.Id,
        entity.RootPath,
        entity.StartedUtc,
        entity.CompletedUtc,
        new ScanSummary(
            entity.FilesScanned,
            entity.TotalSizeBytes,
            entity.FilesRecommendedForDeletion,
            entity.ReclaimableBytes,
            entity.FilesNeedingReview),
        entity.Errors.Select(ToDomain).ToList());

    public static FileScanResult ToDomain(FileScanResultEntity entity) => new(
        entity.FilePath,
        entity.SizeBytes,
        entity.CreatedUtc,
        entity.LastModifiedUtc,
        entity.LastAccessedUtc,
        entity.Recommendation,
        entity.RiskLevel,
        entity.Reason,
        entity.Findings.Select(ToDomain).ToList(),
        entity.RecycledUtc);

    private static PiiFindingEntity ToEntity(PiiFinding finding) => new()
    {
        Category = finding.Category,
        DetectorName = finding.DetectorName,
        MatchCount = finding.MatchCount,
        Confidence = finding.Confidence
    };

    private static ScanErrorEntity ToEntity(ScanError error, Guid reportId) => new()
    {
        ReportId = reportId,
        Path = error.Path,
        Reason = error.Reason
    };

    private static PiiFinding ToDomain(PiiFindingEntity entity) =>
        new(entity.Category, entity.DetectorName, entity.MatchCount, entity.Confidence);

    private static ScanError ToDomain(ScanErrorEntity entity) =>
        new(entity.Path, entity.Reason);
}
