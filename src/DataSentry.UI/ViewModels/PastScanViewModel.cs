using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// One earlier scan, as a row on the Reports tab: where it looked, when, and how much it found.
/// Carries the report itself so that choosing the row can open it without another query.
/// </summary>
public sealed class PastScanViewModel
{
    public PastScanViewModel(ScanReport report)
    {
        Report = report;
        FolderPath = report.RootPath;
        ScannedOnText = $"{report.CompletedUtc.LocalDateTime:d MMM yyyy, HH:mm}";
        SummaryText = PlainLanguage.Headline(report);
        Description = $"{FolderPath} — {ScannedOnText}, {PlainLanguage.Files(report.Summary.FilesScanned)}";
    }

    public ScanReport Report { get; }

    /// <summary>The folder the scan looked at. The row's headline — it is what the user remembers.</summary>
    public string FolderPath { get; }

    /// <summary>"11 Jul 2026, 09:00".</summary>
    public string ScannedOnText { get; }

    /// <summary>"482 files scanned, 3 suggested for deletion, 7 need review."</summary>
    public string SummaryText { get; }

    /// <summary>The row in one line, for anywhere that only has one: "C:\work — 11 Jul 2026, 09:00, 482 files".</summary>
    public string Description { get; }

    public override string ToString() => Description;
}
