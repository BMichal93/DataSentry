using System.IO;
using System.Linq;
using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// One file, as a line the user can read: what it is, where it is, why it was judged the way it was,
/// and what DataSentry suggests doing about it.
/// </summary>
/// <remarks>
/// Nothing here changes after it is built, so there is nothing to notify about — a row is a projection
/// of one <see cref="FileScanResult"/> and lives exactly as long as the page it is on.
/// </remarks>
public sealed class FileRowViewModel
{
    public FileRowViewModel(FileScanResult result)
    {
        FileName = Path.GetFileName(result.FilePath);
        FolderPath = Path.GetDirectoryName(result.FilePath) ?? result.FilePath;
        LastModifiedText = $"Last changed {result.LastModifiedUtc.LocalDateTime:d MMM yyyy}";
        Recommendation = result.Recommendation;
        RecommendationText = Describe(result.Recommendation);
        Reason = result.Reason;

        // The types and the counts. The matched value is not here to be shown, because a PiiFinding
        // does not carry one — the model refuses to hold it, so the screen cannot leak it.
        PiiSummary = PlainLanguage.Findings(result.Findings);
        WhyItMatters = DescribeDanger(result.Findings);
    }

    public string FileName { get; }

    public string FolderPath { get; }

    public string LastModifiedText { get; }

    /// <summary>Bound by the view to tell the three verdicts apart at a glance.</summary>
    public Recommendation Recommendation { get; }

    public string RecommendationText { get; }

    /// <summary>Why, in plain language: "Not opened in 3 years".</summary>
    public string Reason { get; }

    /// <summary>"3 IBANs, 12 email addresses", or empty when the file holds no personal data.</summary>
    public string PiiSummary { get; }

    /// <summary>
    /// Why the findings make the file dangerous, in a sentence: what kind of data it is and what the
    /// exposure is. The kind, never the content — the content stays in the file, where the user's own
    /// access controls already guard it. A report that reprinted it would be a copy of the very data
    /// it exists to police, and anyone shown the report would be shown the data.
    /// </summary>
    public string WhyItMatters { get; }

    public bool HasPii => PiiSummary.Length > 0;

    /// <summary>
    /// One sentence per kind of data found, worst first — the same priority order the recommendation
    /// itself was decided by, so the first line the user reads is the reason the file is on this list.
    /// </summary>
    private static string DescribeDanger(IReadOnlyList<PiiFinding> findings) =>
        string.Join(" ", findings
            .Select(finding => finding.Category)
            .Distinct()
            .OrderBy(category => category)
            .Select(DescribeDanger));

    private static string DescribeDanger(PiiCategory category) => category switch
    {
        PiiCategory.SpecialCategory =>
            "Likely health, beliefs or other special-category data — the kind GDPR holds to the highest bar (Art. 9). Exposing it can harm the person it describes.",
        PiiCategory.Financial =>
            "Bank account or card numbers. They identify a person and their money, and may also be under a legal retention obligation — which is why this is a human decision.",
        PiiCategory.Identity =>
            "National identity numbers. They identify one person for life and are the raw material of identity theft.",
        PiiCategory.Contact =>
            "Names, addresses or other contact details — personal data under GDPR if it points at a person.",
        PiiCategory.Network =>
            "IP addresses, which GDPR treats as personal data when they can be tied to a person.",
        PiiCategory.Keyword =>
            "Terms associated with personal data. Worth a look, though the match is a weak signal on its own.",
        _ => string.Empty
    };

    /// <summary>
    /// "Keep", not "Retain". The enum is written for the developer reading the rules; this is written
    /// for the person deciding what happens to their files.
    /// </summary>
    private static string Describe(Recommendation recommendation) => recommendation switch
    {
        Core.Models.Recommendation.Delete => "Delete",
        Core.Models.Recommendation.Retain => "Keep",
        Core.Models.Recommendation.Review => "Review",
        _ => recommendation.ToString()
    };
}
