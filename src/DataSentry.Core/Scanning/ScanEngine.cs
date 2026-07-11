using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Core.Scanning;

/// <summary>
/// The scan. Walks the tree, classifies every file it finds, streams the verdicts into storage and
/// hands back the report.
/// </summary>
/// <remarks>
/// Two things happen at once and neither waits for the other to finish. The walker fills a bounded
/// channel with the files it finds; the classifier drains it. The bound is what makes a scan of a
/// shared drive survivable: the walker can outrun the classifier — it always does, since finding a
/// file costs nothing and reading one costs a disk seek — and when it does, a full channel makes it
/// wait rather than pulling a million file records into memory ahead of a classifier that is still on
/// the first thousand.
///
/// Nothing a single file can do ends the scan. A file locked by Excel, denied by an ACL, or deleted
/// between being found and being opened is caught here, recorded as a <see cref="ScanError"/>, and the
/// scan moves on to the next one. Only cancellation stops it.
/// </remarks>
public sealed class ScanEngine
{
    /// <summary>
    /// How much of a file a detector gets to see. Personal data in a spreadsheet is in a column, and a
    /// column shows up in the first page of it — reading a two-gigabyte export to the end to find the
    /// same email addresses twice over is a cost with no finding attached to it.
    /// </summary>
    private const int PiiSampleCharacters = 32 * 1024;

    /// <summary>
    /// How many found-but-not-yet-classified files may pile up. Enough that the classifier never sits
    /// idle waiting on the walker, small enough that a tree of any size costs the same handful of
    /// megabytes to scan.
    /// </summary>
    private const int PendingFileCapacity = 1_000;

    private readonly IFileSource _fileSource;
    private readonly IFileContentReader _contentReader;
    private readonly IScanResultStore _resultStore;
    private readonly IReadOnlyList<IClassificationRule> _rules;
    private readonly IReadOnlyList<IPiiDetector> _detectors;
    private readonly DuplicateFileSweep _duplicateSweep;
    private readonly TimeProvider _timeProvider;

    /// <param name="rules">
    /// Consulted in the order they are registered, and the first one with a verdict wins — so junk
    /// ("Temporary file") is registered ahead of staleness ("Not opened in 3 years"), both being true
    /// of the same file but only one of them being the reason a reader needs.
    /// </param>
    /// <param name="duplicateSweep">
    /// Not one of the rules, and it could not be: a rule sees one file, and no file can be a duplicate
    /// on its own. It runs once the results are down, which is the earliest moment at which the question
    /// can even be asked.
    /// </param>
    public ScanEngine(
        IFileSource fileSource,
        IFileContentReader contentReader,
        IScanResultStore resultStore,
        IEnumerable<IClassificationRule> rules,
        IEnumerable<IPiiDetector> detectors,
        DuplicateFileSweep duplicateSweep,
        TimeProvider timeProvider)
    {
        _fileSource = fileSource;
        _contentReader = contentReader;
        _resultStore = resultStore;
        _rules = rules.ToList();
        _detectors = detectors.ToList();
        _duplicateSweep = duplicateSweep;
        _timeProvider = timeProvider;
    }

    /// <summary>Scans the tree and returns the finished report. The per-file results go to the store.</summary>
    public async Task<ScanReport> ScanAsync(
        ScanScope scope,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset startedUtc = _timeProvider.GetUtcNow();

        var errors = new ConcurrentQueue<ScanError>();
        var summary = new ScanSummaryAccumulator();
        var tracker = new ScanProgressTracker(progress);

        Channel<FileMetadata> pendingFiles = Channel.CreateBounded<FileMetadata>(
            new BoundedChannelOptions(PendingFileCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        // The walker's own token, so that a scan ending for any reason — cancelled, or a store that
        // failed to write — never leaves it blocked on a channel nobody is going to read again.
        using var walk = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task walking = WalkAsync(scope, pendingFiles.Writer, errors, tracker, walk.Token);

        var startedReport = new ScanReport(
            Guid.NewGuid(),
            scope.RootPath,
            startedUtc,
            startedUtc,
            summary.ToSummary(),
            []);

        try
        {
            await _resultStore.SaveReportAsync(
                startedReport,
                ClassifyAsync(pendingFiles.Reader, summary, errors, tracker, cancellationToken),
                cancellationToken);
        }
        finally
        {
            walk.Cancel();
            await walking;
        }

        // Only now can this question be asked. Until the last file was walked, any two files still to
        // come might have been copies of each other — and by now every path and size is on disk, where
        // the sweep can group them without pulling the tree back into memory.
        DuplicateSweepResult duplicates = await _duplicateSweep.SweepAsync(
            startedReport.Id,
            errors.Enqueue,
            cancellationToken);

        summary.AddDuplicatesMarkedForDeletion(duplicates);

        var completedReport = startedReport with
        {
            CompletedUtc = _timeProvider.GetUtcNow(),
            Summary = summary.ToSummary(),
            Errors = [.. errors]
        };

        await _resultStore.CompleteReportAsync(completedReport, cancellationToken);

        return completedReport;
    }

    /// <summary>
    /// Fills the channel with everything the tree holds. Never throws: a walk that gave up would take
    /// the whole scan with it, so whatever ends it — cancellation, or the rare failure the walker
    /// itself could not handle — is handed to the classifier by closing the channel behind it.
    /// </summary>
    private async Task WalkAsync(
        ScanScope scope,
        ChannelWriter<FileMetadata> pendingFiles,
        ConcurrentQueue<ScanError> errors,
        ScanProgressTracker tracker,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            IAsyncEnumerable<FileMetadata> files = _fileSource.EnumerateFilesAsync(
                scope,
                errors.Enqueue,
                cancellationToken);

            await foreach (FileMetadata file in files.WithCancellation(cancellationToken))
            {
                tracker.FileDiscovered();
                await pendingFiles.WriteAsync(file, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The classifier is watching the same token and will raise this itself. Reporting it from
            // here as well would only race with it.
        }
        catch (Exception walkFailure)
        {
            failure = walkFailure;
        }
        finally
        {
            pendingFiles.TryComplete(failure);
        }
    }

    private async IAsyncEnumerable<FileScanResult> ClassifyAsync(
        ChannelReader<FileMetadata> pendingFiles,
        ScanSummaryAccumulator summary,
        ConcurrentQueue<ScanError> errors,
        ScanProgressTracker tracker,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (FileMetadata file in pendingFiles.ReadAllAsync(cancellationToken))
        {
            // Between files, and not merely between reads of the channel: cancelling a scan of a
            // shared drive has to stop it now, not once the thousand files already in the channel have
            // been worked through. The same check after the loop catches the scan that was cancelled
            // as its last file went past, so that a stopped scan never ends up looking like a finished one.
            cancellationToken.ThrowIfCancellationRequested();

            FileScanResult? result = await ClassifyFileAsync(file, errors, cancellationToken);

            if (result is null)
            {
                continue;
            }

            summary.Add(result);
            tracker.FileScanned(file.FilePath);

            yield return result;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<FileScanResult?> ClassifyFileAsync(
        FileMetadata file,
        ConcurrentQueue<ScanError> errors,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();

        try
        {
            RuleVerdict? verdict = FirstVerdict(file, nowUtc);
            IReadOnlyList<PiiFinding> findings = await FindPiiAsync(file, errors, cancellationToken);

            FileClassification classification = RecommendationPolicy.Decide(file, verdict, findings, nowUtc);

            return new FileScanResult(
                file.FilePath,
                file.SizeBytes,
                file.CreatedUtc,
                file.LastModifiedUtc,
                file.LastAccessedUtc,
                classification.Recommendation,
                classification.RiskLevel,
                classification.Reason,
                findings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception classificationFailure)
        {
            errors.Enqueue(new ScanError(file.FilePath, classificationFailure.Message));

            return null;
        }
    }

    private RuleVerdict? FirstVerdict(FileMetadata file, DateTimeOffset nowUtc) =>
        _rules
            .Select(rule => rule.Evaluate(file, nowUtc))
            .FirstOrDefault(verdict => verdict is not null);

    private async Task<IReadOnlyList<PiiFinding>> FindPiiAsync(
        FileMetadata file,
        ConcurrentQueue<ScanError> errors,
        CancellationToken cancellationToken)
    {
        if (_detectors.Count == 0 || file.SizeBytes == 0)
        {
            return [];
        }

        string? text;

        try
        {
            text = await _contentReader.ReadTextSampleAsync(file.FilePath, PiiSampleCharacters, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception unreadable)
        {
            // A file that will not open is worth telling the user about, but it is not worth ending
            // the scan over — and its name and timestamps are still there to be judged on.
            errors.Enqueue(new ScanError(file.FilePath, unreadable.Message));

            return [];
        }

        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return [.. _detectors
            .Select(detector => detector.Detect(text))
            .OfType<PiiFinding>()];
    }
}
