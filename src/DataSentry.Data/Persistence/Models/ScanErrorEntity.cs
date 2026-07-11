namespace DataSentry.Data.Persistence.Models;

public sealed class ScanErrorEntity
{
    public long Id { get; set; }

    public Guid ReportId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}
