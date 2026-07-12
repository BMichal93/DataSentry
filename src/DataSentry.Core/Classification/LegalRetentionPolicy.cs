using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// When a document holding personal data has been kept as long as the law typically allows.
/// Not to be confused with <see cref="Retention.RetentionPolicy"/>, which governs how long
/// DataSentry keeps its own scan reports — this one is about the user's documents.
/// </summary>
/// <remarks>
/// DataSentry cannot know the actual obligation attached to a file — retention periods differ by
/// document type and jurisdiction. So the verdict here is a prompt for a human decision, never a
/// legal assertion, and it only ever pushes a file <i>towards</i> Review. A breached retention
/// period is a reason to look, not a licence to purge.
/// </remarks>
public static class LegalRetentionPolicy
{
    /// <summary>
    /// Five years — the conservative end of the typical finance/tax range (invoices and tax records
    /// run five to seven), and the Polish accounting retention period. Conservative here means the
    /// flag raises early rather than late: a false "worth a look" costs a glance, a missed one costs
    /// a document kept without a legal basis.
    /// </summary>
    public const int TypicalRetentionYears = 5;

    /// <summary>How far before the deadline the file starts being worth a look. Six months.</summary>
    public const int ApproachingWindowDays = 182;

    public static RetentionDeadline DeadlineFor(FileMetadata file, DateTimeOffset nowUtc)
    {
        DateTimeOffset deadlineUtc = RetentionClockStart(file).AddYears(TypicalRetentionYears);

        if (nowUtc >= deadlineUtc)
        {
            return RetentionDeadline.Breached;
        }

        return nowUtc >= deadlineUtc.AddDays(-ApproachingWindowDays)
            ? RetentionDeadline.Approaching
            : RetentionDeadline.None;
    }

    /// <summary>What the flag means, as the user should hear it. Empty when there is nothing to say.</summary>
    public static string Describe(RetentionDeadline deadline) => deadline switch
    {
        RetentionDeadline.Breached =>
            $"kept longer than the {TypicalRetentionYears} years documents like this usually must be",
        RetentionDeadline.Approaching =>
            $"approaching the {TypicalRetentionYears}-year mark documents like this are usually kept to",
        _ => string.Empty
    };

    /// <summary>
    /// The later of created and last modified. A retention obligation runs from when the document was
    /// finished, and the last edit is the best signal the file system has for that — a document still
    /// being written has not started its clock, and flagging it would be noise.
    /// </summary>
    private static DateTimeOffset RetentionClockStart(FileMetadata file) =>
        file.LastModifiedUtc > file.CreatedUtc ? file.LastModifiedUtc : file.CreatedUtc;
}
