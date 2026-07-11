using System.Collections.Generic;

namespace DataSentry.Core.Models;

/// <summary>One file, classified — and, if the user has since acted on it, what was done.</summary>
/// <param name="Reason">Why, in plain language: "Not opened in 3 years", not a predicate.</param>
/// <param name="RecycledUtc">
/// When the user sent this file to the recycle bin, or null while it is still on disk.
///
/// <b>This is not a recommendation, and that is the whole reason it is a separate field.</b> The
/// recommendation is what DataSentry advised; this is what the user decided. Folding the second into
/// the first — a <c>Recommendation.Deleted</c> — would have made the row disappear from the very
/// heading it was condemned under, and left the summary counting files under a verdict none of them
/// still carried. A scan says what it found; it does not rewrite itself to agree with what happened
/// next.
/// </param>
public sealed record FileScanResult(
    string FilePath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset LastAccessedUtc,
    Recommendation Recommendation,
    RiskLevel RiskLevel,
    string Reason,
    IReadOnlyList<PiiFinding> Findings,
    DateTimeOffset? RecycledUtc = null)
{
    /// <summary>
    /// Whether this file may be sent to the recycle bin: DataSentry said delete, and nobody has yet.
    /// </summary>
    /// <remarks>
    /// <b>The one gate the delete flow is allowed through, and it lives here rather than in the view
    /// model on purpose.</b> A file that needs review holds personal data — the liability DataSentry
    /// exists to find — and a file marked keep may be under a legal retention obligation. Neither is
    /// the user's to delete on the strength of a scan, and neither becomes so because a checkbox
    /// appeared next to it. The rule that a PII finding overrides a delete recommendation is enforced
    /// where the recommendation is made; this is that same rule, held at the other end.
    /// </remarks>
    public bool CanBeRecycled => Recommendation == Recommendation.Delete && RecycledUtc is null;
}
