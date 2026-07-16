using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Models;
using DataSentry.UI.FileActions;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// One file, as a line the user can read: what it is, where it is, why it was judged the way it was,
/// what DataSentry suggests doing about it — and, if it has been deleted, that it is gone.
/// </summary>
/// <remarks>
/// A row used to be a dead projection of one <see cref="FileScanResult"/> with nothing to notify about.
/// It has state now, because the user can finally do something to it: they can untick it to spare it,
/// and they can send it to the recycle bin. Both are things the row has to be able to say out loud.
/// </remarks>
public sealed class FileRowViewModel : ObservableObject
{
    private readonly IFileOpener _fileOpener;
    private readonly bool _canBeRecycled;

    private bool _isSelectedForDeletion;
    private bool _isRecycled;
    private string _openProblem = string.Empty;

    public FileRowViewModel(FileScanResult result, IFileOpener fileOpener)
    {
        _fileOpener = fileOpener;

        FilePath = result.FilePath;
        FileName = Path.GetFileName(result.FilePath);
        FolderPath = Path.GetDirectoryName(result.FilePath) ?? result.FilePath;
        LastModifiedText = $"Last changed {result.LastModifiedUtc.LocalDateTime:d MMM yyyy}";
        Recommendation = result.Recommendation;
        Reason = result.Reason;

        // The types and the counts. The matched value is not here to be shown, because a PiiFinding
        // does not carry one — the model refuses to hold it, so the screen cannot leak it.
        PiiSummary = PlainLanguage.Findings(result.Findings);
        WhyItMatters = DescribeDanger(result.Findings);
        RetentionAlert = DescribeRetentionDeadline(result.RetentionDeadline);

        // Empty for a report reopened from history: the snippets behind it lived only as long as the
        // process that scanned it did, and a restarted app has nothing left to show — silently, rather
        // than with a message that would draw attention to data that is no longer even in memory.
        Snippets = DescribeSnippets(result.Findings);

        _isRecycled = result.RecycledUtc is not null;

        // Every file can be opened; only a condemned one can be deleted. The gate is the domain's, not
        // the view's — a Review row has no checkbox because CanBeRecycled says so, and it says so
        // because a file holding personal data is not the user's to delete on a scan's say-so.
        _canBeRecycled = result.CanBeRecycled;

        // Ticked by default, and this is the exclusion model in one line: DataSentry has already made
        // the recommendation, so the user is confirming it or carving an exception out of it — not
        // rebuilding it one checkbox at a time.
        _isSelectedForDeletion = CanBeDeleted;

        OpenCommand = new AsyncRelayCommand(OpenAsync);
    }

    /// <summary>The command behind "Open file" — the only way to look at what a flagged file contains.</summary>
    public ICommand OpenCommand { get; }

    public string FilePath { get; }

    public string FileName { get; }

    public string FolderPath { get; }

    public string LastModifiedText { get; }

    /// <summary>Bound by the view to tell the three verdicts apart at a glance.</summary>
    public Recommendation Recommendation { get; }

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
    /// One line per detector that found something in this file — "IBAN: 48*********12, 61**********74"
    /// — for the scan that is still running in this process. Empty for a report reopened from an
    /// earlier session: see the constructor for why that is the correct answer, not a bug.
    /// </summary>
    public IReadOnlyList<string> Snippets { get; }

    public bool HasSnippets => Snippets.Count > 0;

    /// <summary>
    /// Why the retention clock puts this file on the user's desk, in a sentence. Empty for the
    /// overwhelming majority of files, whose personal data — if any — is comfortably inside the
    /// typical legal retention period.
    /// </summary>
    public string RetentionAlert { get; }

    /// <summary>Shown as a badge on the row: this file's retention clock has run out, or nearly has.</summary>
    public bool HasRetentionAlert => RetentionAlert.Length > 0;

    /// <summary>
    /// Whether this row may be sent to the recycle bin at all. False for every Review and Keep row, and
    /// false for a row that has already gone — and when it is false the view shows no checkbox, because
    /// there is no decision here for the user to make.
    /// </summary>
    public bool CanBeDeleted => _canBeRecycled && !IsRecycled;

    /// <summary>
    /// Whether this file is included in the next batch delete. Ticked by default on every deletable row;
    /// unticking it is how the user spares one file without abandoning the other eleven.
    /// </summary>
    public bool IsSelectedForDeletion
    {
        get => _isSelectedForDeletion;
        set => Set(ref _isSelectedForDeletion, value);
    }

    /// <summary>Whether this file has been sent to the recycle bin.</summary>
    public bool IsRecycled
    {
        get => _isRecycled;
        private set
        {
            Set(ref _isRecycled, value);

            Notify(nameof(RecommendationText));
            Notify(nameof(IsStillOnDisk));

            // A file in the recycle bin is not the screen's to offer for deletion a second time — the
            // checkbox goes with the file. This must flip before IsSelectedForDeletion is unticked, so
            // that the untick reads as the deletion it is and not as the user sparing the file.
            Notify(nameof(CanBeDeleted));
        }
    }

    public bool IsStillOnDisk => !IsRecycled;

    /// <summary>
    /// "Keep", not "Retain" — and "Sent to recycle bin" once it has been, because a row that still says
    /// "Delete" after the user has deleted it is telling them their decision did not take.
    /// </summary>
    public string RecommendationText => IsRecycled ? "Sent to recycle bin" : Describe(Recommendation);

    /// <summary>Why the file could not be opened, on the rare occasion it could not. Empty otherwise.</summary>
    public string OpenProblem
    {
        get => _openProblem;
        private set
        {
            Set(ref _openProblem, value);
            Notify(nameof(HasOpenProblem));
        }
    }

    public bool HasOpenProblem => OpenProblem.Length > 0;

    /// <summary>Records that this file went to the recycle bin, without a round trip to the database.</summary>
    public void MarkRecycled()
    {
        IsRecycled = true;
        IsSelectedForDeletion = false;
    }

    private async Task OpenAsync() => OpenProblem = await _fileOpener.OpenAsync(FilePath) ?? string.Empty;

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
    /// One line per detector, worst category first — the same order <see cref="DescribeDanger"/> reads
    /// in — skipping any finding whose snippets did not survive the trip back from the store.
    /// </summary>
    private static IReadOnlyList<string> DescribeSnippets(IReadOnlyList<PiiFinding> findings) =>
        [.. findings
            .Where(finding => finding.RedactedSnippets.Count > 0)
            .OrderBy(finding => finding.Category)
            .Select(finding => $"{finding.DetectorName}: {string.Join(", ", finding.RedactedSnippets)}")];

    private static string DescribeRetentionDeadline(RetentionDeadline deadline) => deadline switch
    {
        RetentionDeadline.Breached =>
            "This file has been kept longer than documents like this usually must be. The legal basis for holding it may have run out — decide whether it still has one.",
        RetentionDeadline.Approaching =>
            "This file is approaching the point where documents like this usually stop having to be kept. Worth deciding its future now, before the clock runs out.",
        _ => string.Empty
    };

    private static string Describe(Recommendation recommendation) => recommendation switch
    {
        Core.Models.Recommendation.Delete => "Delete",
        Core.Models.Recommendation.Retain => "Keep",
        Core.Models.Recommendation.Review => "Review",
        _ => recommendation.ToString()
    };
}
