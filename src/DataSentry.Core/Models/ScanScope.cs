using System.Collections.Generic;

namespace DataSentry.Core.Models;

/// <summary>
/// What a scan is allowed to look at: one root, minus the folders the user has excluded.
/// </summary>
/// <param name="ExcludedPaths">
/// Absolute folder paths that are skipped whole, along with everything beneath them. A full-drive scan
/// that walks into Windows and Program Files spends its time on files no user has any say over, and
/// finds nothing worth deciding about.
/// </param>
public sealed record ScanScope(string RootPath, IReadOnlyList<string> ExcludedPaths)
{
    /// <summary>A scan of everything under the root.</summary>
    public ScanScope(string rootPath)
        : this(rootPath, [])
    {
    }

    /// <summary>Whether the folder — or a folder it sits inside — is excluded from the scan.</summary>
    public bool Excludes(string directoryPath) =>
        ExcludedPaths.Any(excludedPath => IsAtOrUnder(directoryPath, excludedPath));

    private static bool IsAtOrUnder(string directoryPath, string ancestorPath)
    {
        string directory = TrimSeparator(directoryPath);
        string ancestor = TrimSeparator(ancestorPath);

        if (!directory.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "C:/Data" must not exclude "C:/DataSentry": the match has to end on a folder boundary.
        return directory.Length == ancestor.Length
            || directory[ancestor.Length] == Path.DirectorySeparatorChar
            || directory[ancestor.Length] == Path.AltDirectorySeparatorChar;
    }

    private static string TrimSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
