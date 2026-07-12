using System.Collections.Generic;
using DataSentry.Core.Models;

namespace DataSentry.Data.Persistence.Entities;

public sealed class FileScanResultEntity
{
    public long Id { get; set; }

    public Guid ReportId { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastModifiedUtc { get; set; }

    public DateTimeOffset LastAccessedUtc { get; set; }

    public Recommendation Recommendation { get; set; }

    public RiskLevel RiskLevel { get; set; }

    public RetentionDeadline RetentionDeadline { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>When the user sent this file to the recycle bin. Null while it is still on disk.</summary>
    public DateTimeOffset? RecycledUtc { get; set; }

    public List<PiiFindingEntity> Findings { get; set; } = [];
}
