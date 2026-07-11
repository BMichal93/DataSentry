using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DataSentry.UI.FileActions;

/// <summary>
/// Hands the file to Windows and lets it decide which application opens it — the same thing that
/// happens when the user double-clicks it in Explorer.
/// </summary>
public sealed class ShellFileOpener : IFileOpener
{
    public Task<string?> OpenAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult<string?>("That file is no longer there.");
        }

        try
        {
            // UseShellExecute, so Windows picks the application by file type. Without it this would try
            // to run the spreadsheet as a program, which is both useless and exactly the kind of thing
            // a tool holding a list of a user's most sensitive files should not do by accident.
            using Process? opened = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            return Task.FromResult<string?>(null);
        }
        catch (Win32Exception)
        {
            return Task.FromResult<string?>("Windows has no application registered to open that file.");
        }
    }
}
