using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// One reason to delete or keep a file. Each rule is a class of its own, so a new one is added by
/// registering it — never by editing the rules that are already there.
/// </summary>
/// <remarks>
/// A rule sees only <see cref="FileMetadata"/>: the name, the size, the timestamps. Everything the
/// file system hands over for free. Whatever is inside the file is the detectors' business, and a
/// scan should not pay to open a file that its name already condemns.
/// </remarks>
public interface IClassificationRule
{
    /// <summary>The rule's verdict on the file, or null when the rule has nothing to say about it.</summary>
    RuleVerdict? Evaluate(FileMetadata file, DateTimeOffset nowUtc);
}
