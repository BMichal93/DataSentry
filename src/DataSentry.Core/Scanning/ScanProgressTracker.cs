using System.Threading;

namespace DataSentry.Core.Scanning;

/// <summary>
/// Counts what the walker has found and what the classifier has got through. The two run at once and
/// on different threads, which is the whole reason the counters are interlocked rather than incremented.
/// </summary>
internal sealed class ScanProgressTracker
{
    private readonly IProgress<ScanProgress>? _progress;

    private int _filesDiscovered;
    private int _filesScanned;

    public ScanProgressTracker(IProgress<ScanProgress>? progress)
    {
        _progress = progress;
    }

    public void FileDiscovered() =>
        Interlocked.Increment(ref _filesDiscovered);

    public void FileScanned(string filePath)
    {
        int scanned = Interlocked.Increment(ref _filesScanned);

        _progress?.Report(new ScanProgress(
            Volatile.Read(ref _filesDiscovered),
            scanned,
            filePath));
    }
}
