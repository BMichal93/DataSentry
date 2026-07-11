using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// One earlier scan, as a line in the history list: where it looked, when, and how much it found.
/// Carries the report itself so that choosing the line can show it without another query.
/// </summary>
public sealed class PastScanViewModel
{
    public PastScanViewModel(ScanReport report)
    {
        Report = report;
        Description =
            $"{report.RootPath} — {report.CompletedUtc.LocalDateTime:d MMM yyyy HH:mm}, " +
            $"{PlainLanguage.Files(report.Summary.FilesScanned)}";
    }

    public ScanReport Report { get; }

    /// <summary>"C:\work — 11 Jul 2026 09:00, 482 files".</summary>
    public string Description { get; }

    public override string ToString() => Description;
}
