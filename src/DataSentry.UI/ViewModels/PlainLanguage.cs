using System.Collections.Generic;
using System.Linq;
using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// Numbers, turned into the words a person would use for them. "482 files", not a count;
/// "3 IBANs, 12 email addresses", not a list of findings.
/// </summary>
/// <remarks>
/// Every string the user reads about their files is built here, and that is deliberate: the rule that
/// the matched value of a PII finding is never shown is easier to keep when there is one place capable
/// of breaking it. <see cref="Findings"/> is handed the findings and can only reach the type and the
/// count, because <see cref="PiiFinding"/> does not carry the value at all.
/// </remarks>
internal static class PlainLanguage
{
    /// <summary>"1 file", "482 files".</summary>
    public static string Files(int count) => Count(count, "file");

    /// <summary>
    /// The one line that sums a report up: "482 files scanned, 3 suggested for deletion, 7 need review."
    /// Written once because two screens read it — the scan the user just ran, and the same scan reopened
    /// from the Reports tab days later — and the sentence must not drift between them.
    /// </summary>
    public static string Headline(ScanReport report)
    {
        ScanSummary summary = report.Summary;

        // A scan that judged nothing has no headline to give, and "0 files, nothing to delete" reads
        // like an answer when it is really the absence of one. Say which of the two it was: a folder
        // that could not be read is a different problem from a folder with nothing in it.
        if (summary.FilesScanned == 0)
        {
            return report.Errors.Count > 0
                ? "That folder could not be read. Nothing was scanned."
                : "That folder is empty. There is nothing to do.";
        }

        // The headline counts files, not bytes. DataSentry is not here to free disk space — it is here
        // to find the files that are a liability, and "3.1 GB reclaimable" answers a question nobody
        // asked. What can go, and what needs a decision: that is the whole report in one line.
        string deletable = summary.FilesRecommendedForDeletion switch
        {
            0 => "nothing suggested for deletion",
            1 => "1 suggested for deletion",
            _ => $"{summary.FilesRecommendedForDeletion:N0} suggested for deletion"
        };

        string needingReview = summary.FilesNeedingReview switch
        {
            0 => "nothing needs review",
            1 => "1 needs review",
            _ => $"{summary.FilesNeedingReview:N0} need review"
        };

        return $"{Files(summary.FilesScanned)} scanned, {deletable}, {needingReview}.";
    }

    /// <summary>
    /// "3 IBANs, 12 email addresses". The type and the count, never the value — the same rule that binds
    /// the log and the database binds the screen.
    /// </summary>
    public static string Findings(IReadOnlyList<PiiFinding> findings) =>
        string.Join(", ", findings.Select(finding => Count(finding.MatchCount, finding.DetectorName)));

    /// <summary>"1 IBAN", "3 IBANs", "12 email addresses".</summary>
    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count:N0} {Plural(noun)}";

    private static string Plural(string noun) =>
        noun.EndsWith("s") || noun.EndsWith("x") || noun.EndsWith("ch") || noun.EndsWith("sh")
            ? $"{noun}es"
            : $"{noun}s";
}
