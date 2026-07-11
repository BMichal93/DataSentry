using DataSentry.Core.Models;

namespace DataSentry.Data.Persistence.Models;

/// <summary>
/// The type and the count of a finding. There is deliberately no column for the matched value:
/// the database must not become a copy of the data it was built to police.
/// </summary>
public sealed class PiiFindingEntity
{
    public long Id { get; set; }

    public long FileScanResultId { get; set; }

    public PiiCategory Category { get; set; }

    public string DetectorName { get; set; } = string.Empty;

    public int MatchCount { get; set; }

    public double Confidence { get; set; }
}
