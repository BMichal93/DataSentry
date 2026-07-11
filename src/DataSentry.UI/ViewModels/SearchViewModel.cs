using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.UI.Dialogs;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The Search tab: pick a folder, scan it, read the headline, and open any file's row for the detail.
/// The tab holds exactly one search and its result — history lives on the Reports tab, not here.
/// </summary>
/// <remarks>
/// It is handed a <see cref="ScanEngine"/>, its own <see cref="ResultsViewModel"/>, and something that
/// can ask the user for a folder — and it knows nothing beneath any of them: not what a file system is,
/// not that the results are in SQLite, not that any of this runs on Windows. That is what makes it
/// testable without a window, and it is the whole point of the layering.
/// </remarks>
public sealed class SearchViewModel : ObservableObject
{
    /// <summary>
    /// How many unreadable files are listed. A scan of a drive whose permissions are a mess can trip
    /// over thousands, and the thousandth tells the user nothing the tenth did not — the count is the
    /// finding, the list is only the evidence.
    /// </summary>
    private const int MaxUnreadableFilesListed = 100;

    private readonly ScanEngine _scanEngine;
    private readonly IFolderPicker _folderPicker;

    private CancellationTokenSource? _scanCancellation;
    private ScanReport? _report;
    private string _folderPath = string.Empty;
    private string _status = string.Empty;
    private bool _isScanning;
    private bool _isSchedulePanelOpen;

    public SearchViewModel(
        ScanEngine scanEngine,
        ResultsViewModel results,
        ScheduleViewModel schedule,
        IFolderPicker folderPicker)
    {
        _scanEngine = scanEngine;
        _folderPicker = folderPicker;

        Results = results;
        Schedule = schedule;

        BrowseCommand = new AsyncRelayCommand(PickFolderAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !string.IsNullOrWhiteSpace(FolderPath));
        CancelCommand = new RelayCommand(CancelScan, () => _scanCancellation is not null);

        // The schedule hides behind the clock icon until it is asked for: most visits to this screen
        // are here to scan, and a row about a timer would be furniture on every one of them.
        ToggleSchedulePanelCommand = new RelayCommand(() => IsSchedulePanelOpen = !IsSchedulePanelOpen);

        // The schedule needs a folder, and the folder lives here — which is why this command does too.
        ScheduleScanCommand = new AsyncRelayCommand(
            () => Schedule.ScheduleDailyAsync(FolderPath),
            () => !string.IsNullOrWhiteSpace(FolderPath));
    }

    public ICommand BrowseCommand { get; }

    public ICommand ScanCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Opens and closes the schedule panel behind the clock icon.</summary>
    public ICommand ToggleSchedulePanelCommand { get; }

    /// <summary>Schedules the folder in the box for a daily scan at the hour in the schedule panel.</summary>
    public ICommand ScheduleScanCommand { get; }

    /// <summary>The result list. Filled the moment a scan finishes; each row expands when clicked.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>The daily scheduled scan, if the user has set one.</summary>
    public ScheduleViewModel Schedule { get; }

    public string FolderPath
    {
        get => _folderPath;
        set => Set(ref _folderPath, value);
    }

    /// <summary>The one line the user reads. Plain language, never a predicate.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            Set(ref _status, value);
            Notify(nameof(HasStatus));
        }
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>Drives the progress bar and the Cancel button, and nothing else.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    /// <summary>Whether a finished scan is on screen — there is no list to show before there is.</summary>
    public bool HasResults => _report is not null;

    /// <summary>
    /// Whether the result list is worth screen space. A scan that judged nothing has a headline
    /// ("that folder is empty") but no files to put under it, and an empty list under three zeroed
    /// filter chips would only make the answer look unfinished.
    /// </summary>
    public bool HasFilesToShow => _report is not null && _report.Summary.FilesScanned > 0;

    /// <summary>Whether the schedule panel is open. The clock icon is the only thing that moves it.</summary>
    public bool IsSchedulePanelOpen
    {
        get => _isSchedulePanelOpen;
        private set => Set(ref _isSchedulePanelOpen, value);
    }

    /// <summary>
    /// The files the scan could not open — locked, denied, path too long. The user is told they exist,
    /// because a scan that quietly skipped a thousand files has told them a comfortable lie about what
    /// is on their drive. It is a footnote and not the headline: nothing here is a decision they have
    /// to make, only something DataSentry could not see.
    /// </summary>
    public IReadOnlyList<ScanError> UnreadableFiles { get; private set; } = [];

    public bool HasUnreadableFiles => UnreadableFiles.Count > 0;

    public string UnreadableFilesSummary
    {
        get
        {
            int total = _report?.Errors.Count ?? 0;

            string headline = total == 1
                ? "1 file could not be read, and was not judged."
                : $"{total:N0} files could not be read, and were not judged.";

            return total > MaxUnreadableFilesListed
                ? $"{headline} The first {MaxUnreadableFilesListed} are listed."
                : headline;
        }
    }

    /// <summary>
    /// Asks the user for a folder. What opens to ask them is not this class's business — that is the
    /// whole reason <see cref="IFolderPicker"/> exists.
    /// </summary>
    public async Task PickFolderAsync()
    {
        string? folderPath = await _folderPicker.PickFolderAsync();

        if (folderPath is not null)
        {
            FolderPath = folderPath;
        }
    }

    /// <summary>
    /// Scans the folder, puts the headline in <see cref="Status"/> and the files behind it straight
    /// into <see cref="Results"/> — a search that made the user ask to see its results would not be
    /// much of a search. Public, and not buried inside the command, so that a test can await it: an
    /// <see cref="ICommand"/> returns void by design and there is nothing in it to wait for.
    /// </summary>
    public async Task ScanAsync()
    {
        _scanCancellation = new CancellationTokenSource();

        IsScanning = true;

        // Reported synchronously, not through Progress<T>: Progress posts its callbacks, and a posted
        // callback can land after the scan has finished and overwrite the summary with "Scanned 3 of
        // 3…". Status is a scalar property, and WPF marshals a scalar change onto the UI thread itself.
        var progress = new SynchronousProgress<ScanProgress>(scanProgress =>
            Status = $"Scanned {scanProgress.FilesScanned:N0} of {scanProgress.FilesDiscovered:N0} files found so far…");

        try
        {
            ScanReport report = await _scanEngine.ScanAsync(
                new ScanScope(FolderPath),
                progress,
                _scanCancellation.Token);

            await ShowReportAsync(report);
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled. Nothing was changed.";
        }
        finally
        {
            IsScanning = false;

            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>Stops a running scan. Nothing is written and nothing is deleted; there is nothing to undo.</summary>
    public void CancelScan() => _scanCancellation?.Cancel();

    private async Task ShowReportAsync(ScanReport report)
    {
        _report = report;

        Status = PlainLanguage.Headline(report);
        UnreadableFiles = [.. report.Errors.Take(MaxUnreadableFilesListed)];

        Notify(nameof(HasResults));
        Notify(nameof(HasFilesToShow));
        Notify(nameof(UnreadableFiles));
        Notify(nameof(HasUnreadableFiles));
        Notify(nameof(UnreadableFilesSummary));

        if (HasFilesToShow)
        {
            await Results.LoadAsync(report.Id, report.Summary);
        }
    }

    /// <summary>
    /// An <see cref="IProgress{T}"/> that reports on the caller's thread, now. The built-in
    /// <see cref="Progress{T}"/> posts instead, and a post can arrive after the work is done — which
    /// here means a progress line stamping over the finished summary.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
