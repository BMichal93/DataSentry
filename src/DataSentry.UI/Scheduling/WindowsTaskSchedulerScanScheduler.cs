using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DataSentry.UI.Scheduling;

/// <summary>
/// The schedule, kept by Windows Task Scheduler. Windows does the waking: the task starts DataSentry
/// with <c>--scan "folder"</c> at the appointed hour whether or not the app is open, which is the whole
/// reason this is a scheduled task and not a timer inside a window somebody has to leave running.
/// </summary>
/// <remarks>
/// It talks to <c>schtasks.exe</c>, which has shipped with every Windows since XP and needs no
/// elevation to manage a task in the user's own session. The task is registered under a fixed name,
/// so scheduling twice replaces rather than accumulates.
/// </remarks>
public sealed class WindowsTaskSchedulerScanScheduler : IScanScheduler
{
    private const string TaskName = "DataSentry daily scan";

    public async Task<ScheduledScan?> GetScheduledScanAsync()
    {
        (int exitCode, string taskXml) = await RunSchtasksAsync("/Query", "/TN", TaskName, "/XML");

        // A non-zero exit means the task does not exist — which is not an error, it is the answer.
        return exitCode == 0 ? ParseTask(taskXml) : null;
    }

    public async Task ScheduleDailyScanAsync(string folderPath, TimeOnly startTime)
    {
        // The action the task runs: this very executable, in headless mode, on that folder.
        string command = $"\"{Environment.ProcessPath}\" {HeadlessScan.ScanArgument} \"{folderPath}\"";

        (int exitCode, string output) = await RunSchtasksAsync(
            "/Create", "/F",
            "/SC", "DAILY",
            "/TN", TaskName,
            "/ST", startTime.ToString("HH\\:mm", CultureInfo.InvariantCulture),
            "/TR", command);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Windows would not create the scheduled task: {output.Trim()}");
        }
    }

    public async Task RemoveScheduledScanAsync()
    {
        // Removing a schedule that is not there is not a failure; the user asked for none, and none
        // is what there is.
        await RunSchtasksAsync("/Delete", "/F", "/TN", TaskName);
    }

    /// <summary>
    /// Reads the schedule back out of the task's XML: the start time from its trigger, the folder from
    /// the quoted argument after <c>--scan</c> in its action.
    /// </summary>
    private static ScheduledScan? ParseTask(string taskXml)
    {
        try
        {
            XDocument task = XDocument.Parse(taskXml);

            string? startBoundary = ElementValue(task, "StartBoundary");
            string? arguments = ElementValue(task, "Arguments");

            if (startBoundary is null || arguments is null)
            {
                return null;
            }

            string? folderPath = FolderPathFrom(arguments);

            if (folderPath is null)
            {
                return null;
            }

            var startTime = TimeOnly.FromDateTime(DateTime.Parse(startBoundary, CultureInfo.InvariantCulture));

            return new ScheduledScan(folderPath, startTime);
        }
        catch (FormatException)
        {
            // A task under our name but not of our making. Treat it as no schedule rather than crash
            // the startup over it.
            return null;
        }
    }

    private static string? ElementValue(XDocument document, string localName) =>
        document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

    /// <summary>The path between the quotes of <c>--scan "C:\folder"</c>.</summary>
    private static string? FolderPathFrom(string arguments)
    {
        int scanFlag = arguments.IndexOf(HeadlessScan.ScanArgument, StringComparison.OrdinalIgnoreCase);

        if (scanFlag < 0)
        {
            return null;
        }

        int openingQuote = arguments.IndexOf('"', scanFlag);
        int closingQuote = openingQuote < 0 ? -1 : arguments.IndexOf('"', openingQuote + 1);

        return closingQuote < 0 ? null : arguments[(openingQuote + 1)..closingQuote];
    }

    private static async Task<(int ExitCode, string Output)> RunSchtasksAsync(params string[] arguments)
    {
        var schtasks = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            schtasks.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(schtasks)
            ?? throw new InvalidOperationException("schtasks.exe would not start.");

        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, standardOutput + standardError);
    }
}
