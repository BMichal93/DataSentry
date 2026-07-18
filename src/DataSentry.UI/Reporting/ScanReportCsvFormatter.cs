using System.Linq;
using DataSentry.Core.Models;
using DataSentry.UI.ViewModels;

namespace DataSentry.UI.Reporting;

/// <summary>
/// One file's scan result, as a CSV row. The same rule that governs every screen governs this file: the
/// matched value has no column here, only the type, the count, and a redacted shape of each match.
/// </summary>
internal static class ScanReportCsvFormatter
{
    public const string Header = "Path,Recommendation,Reason,PII findings,Redacted snippets";

    public static string FormatRow(FileScanResult result) => string.Join(
        ",",
        Escape(result.FilePath),
        Escape(result.Recommendation.ToString()),
        Escape(result.Reason),
        Escape(PlainLanguage.Findings(result.Findings)),
        Escape(FormatSnippets(result.Findings)));

    /// <summary>
    /// "IBAN: 48*********12; email address: an***le@***.pl" — one clause per detector, worst category
    /// first, exactly as the detail pane on screen reads. Empty for a report reopened from an earlier
    /// session, whose snippets did not survive the restart.
    /// </summary>
    private static string FormatSnippets(IReadOnlyList<PiiFinding> findings) =>
        string.Join(
            "; ",
            findings
                .Where(finding => finding.RedactedSnippets.Count > 0)
                .OrderBy(finding => finding.Category)
                .Select(finding => $"{finding.DetectorName}: {string.Join(", ", finding.RedactedSnippets)}"));

    /// <summary>Quoted, with embedded quotes doubled, whenever a comma, quote or line break would otherwise break the column.</summary>
    private static string Escape(string value)
    {
        bool needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
