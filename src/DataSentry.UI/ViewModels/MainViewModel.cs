using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.UI.Dialogs;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// What the main window shows and what its buttons do: pick a folder, scan it, read the headline, and
/// look at the files behind it if you want to.
/// </summary>
/// <remarks>
/// It is handed a <see cref="ScanEngine"/>, a store it reads the results back from, and something that
/// can ask the user for a folder — and it knows nothing beneath any of them: not what a file system is,
/// not that the results are in SQLite, not that any of this runs on Windows. That is what makes it
/// testable without a window, and it is the whole point of the layering.
/// </remarks>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>
    /// How many unreadable files are listed. A scan of a drive whose permissions are a mess can trip
    /// over thousands, and the thousandth tells the user nothing the tenth did not — the count is the
    /// finding, the list is only the evidence.
    /// </summary>
    private const int MaxUnreadableFilesListed = 100;

    private readonly ScanEngine _scanEngine;
    private readonly IScanResultStore _resultStore;
    private readonly IFolderPicker _folderPicker;

    private CancellationTokenSource? _scanCancellation;
    private ScanReport? _report;
    private PastScanViewModel? _selectedPastScan;
    private string _folderPath = string.Empty;
    private string _status = "Pick a folder and scan it.";
    private bool _isScanning;
    private bool _isDetailVisible;

    public MainViewModel(
        ScanEngine scanEngine,
        IScanResultStore resultStore,
        ResultsViewModel results,
        ScheduleViewModel schedule,
        IFolderPicker folderPicker)
    {
        _scanEngine = scanEngine;
        _resultStore = resultStore;
        _folderPicker = folderPicker;

        Results = results;
        Schedule = schedule;

        BrowseCommand = new AsyncRelayCommand(PickFolderAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !string.IsNullOrWhiteSpace(FolderPath));
        CancelCommand = new RelayCommand(CancelScan, () => _scanCancellation is not null);
        ShowDetailCommand = new AsyncRelayCommand(ToggleDetailAsync, () => HasResults);

        // The schedule needs a folder, and the folder lives here — which is why this command does too.
        ScheduleScanCommand = new AsyncRelayCommand(
            () => Schedule.ScheduleDailyAsync(FolderPath),
            () => !string.IsNullOrWhiteSpace(FolderPath));
    }

    public ICommand BrowseCommand { get; }

    public ICommand ScanCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Progressive disclosure: the summary is the answer, and the files behind it are there if asked for.</summary>
    public ICommand ShowDetailCommand { get; }

    /// <summary>The detail list. Empty until the user asks to see it.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>The daily scheduled scan, if the user has set one.</summary>
    public ScheduleViewModel Schedule { get; }

    /// <summary>Schedules the folder in the box for a daily scan at the hour in the schedule row.</summary>
    public ICommand ScheduleScanCommand { get; }

    /// <summary>
    /// The scans still in the database, newest first. Reports are purged after 30 days, so this is the
    /// last month of history — the purge bounds the list, not the screen.
    /// </summary>
    public ObservableCollection<PastScanViewModel> PastScans { get; } = [];

    public bool HasPastScans => PastScans.Count > 0;

    /// <summary>
    /// The earlier scan the user picked from the history list. Choosing one shows its report exactly
    /// as it was shown the day it ran — no query, because the summary travels on the report itself.
    /// </summary>
    public PastScanViewModel? SelectedPastScan
    {
        get => _selectedPastScan;
        set
        {
            Set(ref _selectedPastScan, value);

            if (value is null)
            {
                return;
            }

            FolderPath = value.Report.RootPath;
            ShowReport(value.Report);
        }
    }

    public string FolderPath
    {
        get => _folderPath;
        set => Set(ref _folderPath, value);
    }

    /// <summary>The one line the user reads. Plain language, never a predicate.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Drives the progress bar and the Cancel button, and nothing else.</summary>
    public bool IsScanning
    {
        get => _isScanning;
        private set => Set(ref _isScanning, value);
    }

    /// <summary>Whether a finished scan is on screen — there is nothing to disclose before there is.</summary>
    public bool HasResults => _report is not null;

    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        private set
        {
            Set(ref _isDetailVisible, value);
            Notify(nameof(DetailToggleText));
        }
    }

    public string DetailToggleText => IsDetailVisible ? "Hide the files" : "Show the files";

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
    /// Fills the history list and reads back any existing schedule. Called once at startup, by the
    /// composition root — the code-behind assigns a DataContext and does nothing else, so it cannot be
    /// the one to call this.
    /// </summary>
    public async Task LoadAsync()
    {
        await RefreshPastScansAsync();
        await Schedule.LoadAsync();
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
    /// Scans the folder and puts the headline in <see cref="Status"/>. Public, and not buried inside
    /// the command, so that a test can await it: an <see cref="ICommand"/> returns void by design and
    /// there is nothing in it to wait for.
    /// </summary>
    public async Task ScanAsync()
    {
        _scanCancellation = new CancellationTokenSource();

        IsScanning = true;
        HideDetail();

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

            ShowReport(report);
            await RefreshPastScansAsync();
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

    /// <summary>
    /// Shows the files behind the headline, or puts them away again. The first page is not fetched until
    /// the user asks for it: for most scans the headline is the whole answer, and a query nobody wanted
    /// is a query not worth making.
    /// </summary>
    public async Task ToggleDetailAsync()
    {
        if (IsDetailVisible)
        {
            HideDetail();
            return;
        }

        if (_report is null)
        {
            return;
        }

        await Results.LoadAsync(_report.Id, _report.Summary);

        IsDetailVisible = true;
    }

    private void ShowReport(ScanReport report)
    {
        _report = report;

        Status = Describe(report);
        UnreadableFiles = [.. report.Errors.Take(MaxUnreadableFilesListed)];

        Notify(nameof(HasResults));
        Notify(nameof(UnreadableFiles));
        Notify(nameof(HasUnreadableFiles));
        Notify(nameof(UnreadableFilesSummary));
    }

    private void HideDetail() => IsDetailVisible = false;

    /// <summary>
    /// An <see cref="IProgress{T}"/> that reports on the caller's thread, now. The built-in
    /// <see cref="Progress{T}"/> posts instead, and a post can arrive after the work is done — which
    /// here means a progress line stamping over the finished summary.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private async Task RefreshPastScansAsync()
    {
        IReadOnlyList<ScanReport> reports = await _resultStore.ListReportsAsync();

        // The selection is cleared through the field, not the property: a refresh must never re-open
        // a report the user did not just choose.
        _selectedPastScan = null;
        Notify(nameof(SelectedPastScan));

        PastScans.Clear();

        foreach (ScanReport report in reports)
        {
            PastScans.Add(new PastScanViewModel(report));
        }

        Notify(nameof(HasPastScans));
    }

    /// <summary>"482 files, 3.1 GB reclaimable, 7 files need review."</summary>
    private static string Describe(ScanReport report)
    {
        ScanSummary summary = report.Summary;

        // A scan that judged nothing has no headline to give, and "0 files, 0 bytes reclaimable" reads
        // like an answer when it is really the absence of one. Say which of the two it was: a folder
        // that could not be read is a different problem from a folder with nothing in it.
        if (summary.FilesScanned == 0)
        {
            return report.Errors.Count > 0
                ? "That folder could not be read. Nothing was scanned."
                : "That folder is empty. There is nothing to do.";
        }

        // The headline counts files, not bytes. DataSentry is not here to free disk space — it is here
        // to find the files that are a liability, and "3.1 GB reclaimable" answers a question nobody
        // asked. What can go, and what needs a decision: that is the whole report in one line.
        string deletable = summary.FilesRecommendedForDeletion switch
        {
            0 => "nothing suggested for deletion",
            1 => "1 suggested for deletion",
            _ => $"{summary.FilesRecommendedForDeletion:N0} suggested for deletion"
        };

        string needingReview = summary.FilesNeedingReview switch
        {
            0 => "nothing needs review",
            1 => "1 needs review",
            _ => $"{summary.FilesNeedingReview:N0} need review"
        };

        return $"{PlainLanguage.Files(summary.FilesScanned)} scanned, {deletable}, {needingReview}.";
    }
}
