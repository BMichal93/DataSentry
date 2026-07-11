using System.Collections.Generic;
using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// Files that were never meant to be kept: the leftovers of a crashed editor, a half-finished
/// download, a thumbnail cache. Their name alone gives them away, so no file is ever opened for this.
/// </summary>
public sealed class JunkFileRule : IClassificationRule
{
    private static readonly Dictionary<string, string> DisposableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".tmp"] = "Temporary file",
        [".temp"] = "Temporary file",
        [".bak"] = "Backup file",
        [".old"] = "Superseded copy",
        [".dmp"] = "Crash dump",
        [".chk"] = "Recovered disk fragment",
        [".part"] = "Unfinished download",
        [".partial"] = "Unfinished download",
        [".crdownload"] = "Unfinished download"
    };

    private static readonly Dictionary<string, string> DisposableFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["thumbs.db"] = "Thumbnail cache",
        ["desktop.ini"] = "Folder view settings",
        [".ds_store"] = "Folder view settings"
    };

    /// <summary>Word and Excel leave one of these behind for every document open in an editor that crashed.</summary>
    private const string OfficeLockFilePrefix = "~$";

    public RuleVerdict? Evaluate(FileMetadata file, DateTimeOffset nowUtc)
    {
        if (file.SizeBytes == 0)
        {
            return new RuleVerdict(Recommendation.Delete, "Empty file");
        }

        if (file.FileName.StartsWith(OfficeLockFilePrefix, StringComparison.Ordinal))
        {
            return new RuleVerdict(Recommendation.Delete, "Leftover Office lock file");
        }

        if (DisposableFileNames.TryGetValue(file.FileName, out string? knownFileReason))
        {
            return new RuleVerdict(Recommendation.Delete, knownFileReason);
        }

        if (DisposableExtensions.TryGetValue(file.Extension, out string? knownExtensionReason))
        {
            return new RuleVerdict(Recommendation.Delete, $"{knownExtensionReason} ({file.Extension})");
        }

        return null;
    }
}
