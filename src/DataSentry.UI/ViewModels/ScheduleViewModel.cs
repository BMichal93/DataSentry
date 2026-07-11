using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.UI.Scheduling;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The one scheduled scan: whether it exists, when it fires, and the two things that can be done about
/// it — set it, remove it. It talks to <see cref="IScanScheduler"/> and never learns that Windows Task
/// Scheduler stands behind it, which is what lets a test schedule scans all day without touching one.
/// </summary>
public sealed class ScheduleViewModel : ObservableObject
{
    /// <summary>"Just before I leave work" is the use case, so the box starts at the end of a workday.</summary>
    private const string DefaultStartTime = "17:00";

    private readonly IScanScheduler _scheduler;

    private ScheduledScan? _scheduledScan;
    private string _startTimeText = DefaultStartTime;
    private string _message = string.Empty;

    public ScheduleViewModel(IScanScheduler scheduler)
    {
        _scheduler = scheduler;

        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => HasSchedule);
    }

    public ICommand RemoveCommand { get; }

    /// <summary>What the user typed into the time box. Parsed, never trusted.</summary>
    public string StartTimeText
    {
        get => _startTimeText;
        set => Set(ref _startTimeText, value);
    }

    public bool HasSchedule => _scheduledScan is not null;

    public bool HasNoSchedule => !HasSchedule;

    /// <summary>"Scans C:\work every day at 17:00." Empty when nothing is scheduled.</summary>
    public string Description =>
        _scheduledScan is null
            ? string.Empty
            : $"Scans {_scheduledScan.FolderPath} every day at " +
              $"{_scheduledScan.StartTime.ToString("HH\\:mm", CultureInfo.InvariantCulture)}.";

    /// <summary>What went wrong, in a sentence, when something did. Empty the rest of the time.</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>Reads the schedule that may already exist. Called once at startup, like the history.</summary>
    public async Task LoadAsync()
    {
        _scheduledScan = await _scheduler.GetScheduledScanAsync();

        NotifyScheduleChanged();
    }

    /// <summary>Schedules the daily scan of a folder, replacing whatever schedule was there before.</summary>
    public async Task ScheduleDailyAsync(string folderPath)
    {
        if (!TimeOnly.TryParseExact(StartTimeText.Trim(), ["HH:mm", "H:mm"], out TimeOnly startTime))
        {
            Message = $"\"{StartTimeText}\" is not a time DataSentry understands. Try {DefaultStartTime}.";
            return;
        }

        try
        {
            await _scheduler.ScheduleDailyScanAsync(folderPath, startTime);
        }
        catch (InvalidOperationException schedulingRefused)
        {
            // Windows saying no is an environment problem, not a bug — put the sentence on the screen
            // and leave the app standing.
            Message = schedulingRefused.Message;
            return;
        }

        _scheduledScan = new ScheduledScan(folderPath, startTime);
        Message = string.Empty;

        NotifyScheduleChanged();
    }

    public async Task RemoveAsync()
    {
        await _scheduler.RemoveScheduledScanAsync();

        _scheduledScan = null;
        Message = string.Empty;

        NotifyScheduleChanged();
    }

    private void NotifyScheduleChanged()
    {
        Notify(nameof(HasSchedule));
        Notify(nameof(HasNoSchedule));
        Notify(nameof(Description));
    }
}
