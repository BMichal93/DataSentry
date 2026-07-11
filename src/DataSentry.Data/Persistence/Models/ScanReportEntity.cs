using System.Collections.Generic;

namespace DataSentry.Data.Persistence.Models;

/// <summary>The persistence shape of a scan report. Stays inside the data layer; Core never sees it.</summary>
public sealed class ScanReportEntity
{
    public Guid Id { get; set; }

    public string RootPath { get; set; } = string.Empty;

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset CompletedUtc { get; set; }

    public int FilesScanned { get; set; }

    public long TotalSizeBytes { get; set; }

    public int FilesRecommendedForDeletion { get; set; }

    public long ReclaimableBytes { get; set; }

    public int FilesNeedingReview { get; set; }

    public List<FileScanResultEntity> Results { get; set; } = [];

    public List<ScanErrorEntity> Errors { get; set; } = [];
}
