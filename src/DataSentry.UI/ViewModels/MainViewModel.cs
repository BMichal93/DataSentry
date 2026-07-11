using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// What the main window shows and what its buttons do. It is handed a <see cref="ScanEngine"/> and
/// knows nothing else: not what a file system is, not that there is a database, not that any of it
/// runs on Windows. That is what makes it testable without a window, and it is the whole point of the
/// layering — the view model talks to the business layer and to nothing underneath it.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ScanEngine _scanEngine;

    private CancellationTokenSource? _scanCancellation;
    private string _folderPath = string.Empty;
    private string _status = "Pick a folder and scan it.";

    public MainViewModel(ScanEngine scanEngine)
    {
        _scanEngine = scanEngine;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !string.IsNullOrWhiteSpace(FolderPath));
        CancelCommand = new RelayCommand(CancelScan, () => _scanCancellation is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ScanCommand { get; }

    public ICommand CancelCommand { get; }

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

    /// <summary>
    /// Scans the folder and puts the headline in <see cref="Status"/>. Public, and not buried inside
    /// the command, so that a test can await it: an <see cref="ICommand"/> returns void by design and
    /// there is nothing in it to wait for.
    /// </summary>
    public async Task ScanAsync()
    {
        _scanCancellation = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(scanProgress =>
            Status = $"Scanned {scanProgress.FilesScanned} of {scanProgress.FilesDiscovered} files found so far…");

        try
        {
            ScanReport report = await _scanEngine.ScanAsync(
                new ScanScope(FolderPath),
                progress,
                _scanCancellation.Token);

            Status = Describe(report);
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled. Nothing was changed.";
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>Stops a running scan. Nothing is written and nothing is deleted; there is nothing to undo.</summary>
    public void CancelScan() => _scanCancellation?.Cancel();

    /// <summary>"482 files, 3.1 GB reclaimable, 7 files need review."</summary>
    private static string Describe(ScanReport report)
    {
        ScanSummary summary = report.Summary;

        string filesScanned = summary.FilesScanned == 1 ? "1 file" : $"{summary.FilesScanned:N0} files";

        string needingReview = summary.FilesNeedingReview == 1
            ? "1 file needs review"
            : $"{summary.FilesNeedingReview:N0} files need review";

        return $"{filesScanned}, {Describe(summary.ReclaimableBytes)} reclaimable, {needingReview}.";
    }

    private static string Describe(long sizeBytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];

        double size = sizeBytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{sizeBytes} bytes" : $"{size:0.#} {units[unit]}";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
