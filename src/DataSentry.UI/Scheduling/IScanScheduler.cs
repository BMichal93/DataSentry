using System.Threading.Tasks;

namespace DataSentry.UI.Scheduling;

/// <summary>
/// The one scheduled scan DataSentry maintains: one folder, once a day. One, because the tool has one
/// obvious thing to do — a list of schedules is a settings screen wearing a different hat.
/// </summary>
public sealed record ScheduledScan(string FolderPath, TimeOnly StartTime);

/// <summary>
/// Keeps the scheduled scan with the operating system, so it fires whether or not DataSentry is open.
/// </summary>
/// <remarks>
/// The same line <see cref="Dialogs.IFolderPicker"/> draws, drawn again: registering a scheduled task
/// is a conversation with Windows, and a view model that held it directly could not be tested without
/// one. The view model asks for the schedule to exist; only the composition root knows that Windows
/// Task Scheduler is what makes it true.
/// </remarks>
public interface IScanScheduler
{
    /// <summary>The current schedule, or null when there is none.</summary>
    Task<ScheduledScan?> GetScheduledScanAsync();

    /// <summary>Schedules the daily scan, replacing any schedule that was there before.</summary>
    Task ScheduleDailyScanAsync(string folderPath, TimeOnly startTime);

    Task RemoveScheduledScanAsync();
}
