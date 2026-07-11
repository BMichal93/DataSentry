using System.Collections.Generic;

namespace DataSentry.UI.Scheduling;

/// <summary>
/// Recognises the command line the scheduled task starts DataSentry with: <c>--scan "C:\folder"</c>.
/// A launch that carries it runs the scan and exits without ever opening a window; the report lands in
/// the store and is waiting in the history list the next time the app is opened by a person.
/// </summary>
public static class HeadlessScan
{
    public const string ScanArgument = "--scan";

    /// <summary>The folder the launch asks to scan, or null when this is an ordinary launch.</summary>
    public static string? FolderPathFrom(IReadOnlyList<string> commandLineArguments)
    {
        for (int index = 0; index < commandLineArguments.Count - 1; index++)
        {
            if (string.Equals(commandLineArguments[index], ScanArgument, StringComparison.OrdinalIgnoreCase))
            {
                return commandLineArguments[index + 1];
            }
        }

        return null;
    }
}
