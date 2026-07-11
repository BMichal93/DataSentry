using System.IO;
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
        SizeText = PlainLanguage.Size(result.SizeBytes);
        LastModifiedText = $"Last changed {result.LastModifiedUtc.LocalDateTime:d MMM yyyy}";
        Recommendation = result.Recommendation;
        RecommendationText = Describe(result.Recommendation);
        Reason = result.Reason;

        // The types and the counts. The matched value is not here to be shown, because a PiiFinding
        // does not carry one — the model refuses to hold it, so the screen cannot leak it.
        PiiSummary = PlainLanguage.Findings(result.Findings);
    }

    public string FileName { get; }

    public string FolderPath { get; }

    public string SizeText { get; }

    public string LastModifiedText { get; }

    /// <summary>Bound by the view to tell the three verdicts apart at a glance.</summary>
    public Recommendation Recommendation { get; }

    public string RecommendationText { get; }

    /// <summary>Why, in plain language: "Not opened in 3 years".</summary>
    public string Reason { get; }

    /// <summary>"3 IBANs, 12 email addresses", or empty when the file holds no personal data.</summary>
    public string PiiSummary { get; }

    public bool HasPii => PiiSummary.Length > 0;

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
