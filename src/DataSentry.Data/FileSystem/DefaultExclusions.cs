using System.Collections.Generic;
using System.IO;

namespace DataSentry.Data.FileSystem;

/// <summary>
/// The folders a scan starts out excluding. They are a starting point, not a rule — the user can see
/// this list and edit it, which is why it lives here as a default rather than as policy in Core.
/// </summary>
public static class DefaultExclusions
{
    /// <summary>
    /// Windows itself and the programs installed on it. Nothing in here is the user's to delete, and
    /// walking it costs minutes on a full-drive scan.
    /// </summary>
    public static IReadOnlyList<string> ForThisMachine()
    {
        string[] excludedFolders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        ];

        return [.. excludedFolders.Where(folder => !string.IsNullOrEmpty(folder)).Distinct()];
    }
}
