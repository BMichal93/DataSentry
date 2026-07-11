namespace DataSentry.Core.Models;

/// <summary>
/// A file that shares its size with at least one other file in the same scan, and so might turn out to
/// be a copy of it. Everything the duplicate sweep needs to judge it, and nothing else — a candidate
/// carries no findings and no reason, because a million of these can pass through a sweep and none of
/// them should cost more than they have to.
/// </summary>
/// <param name="CreatedUtc">Which of a set of identical files is the original: the one that existed first.</param>
/// <param name="Recommendation">What the classification rules already made of the file.</param>
/// <param name="HoldsPersonalData">
/// Whether a detector found anything in it. A copy holding personal data is still a copy, but it is
/// never deleted for it — the finding overrides the delete, exactly as it does everywhere else.
/// </param>
public sealed record DuplicateCandidate(
    string FilePath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    Recommendation Recommendation,
    bool HoldsPersonalData);
