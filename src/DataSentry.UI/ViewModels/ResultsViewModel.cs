using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.UI.Dialogs;
using DataSentry.UI.FileActions;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The detail list: one page of files at a time, all carrying the same recommendation — and, under the
/// deletion heading, the one place in DataSentry where a file is actually destroyed.
/// </summary>
/// <remarks>
/// <b>The list is bounded, and staying bounded is its whole job.</b> A scan of a shared drive can write
/// a million rows, and no arrangement of them is worth freezing the window for. So the view model holds
/// <see cref="PageSize"/> of them and no more: it asks the store for the page it is showing, and the
/// store does the skipping in SQLite, where the rows already are. The totals under each heading are read
/// off the summary the scan already counted, so even knowing how many there are costs nothing.
///
/// The alternative — stream every result into an <see cref="ObservableCollection{T}"/> and let WPF
/// virtualise the rendering — was rejected: UI virtualisation spares the window from drawing a million
/// rows, but not the process from holding them, and the engine has spent its whole life refusing to hold
/// a tree in memory. It would be undone here, on the last screen, for a list nobody can read anyway.
///
/// <b>The delete lives here rather than on the main window because the heading does.</b> "Suggested for
/// deletion" is a thing this class owns, knows the count of, and can name the files behind — and an
/// action belongs with the list it acts on, not one level up from it.
/// </remarks>
public sealed class ResultsViewModel : ObservableObject
{
    /// <summary>
    /// How many files are on a page. More than a screenful, so scrolling feels continuous; nowhere near
    /// enough to be worth worrying about the memory.
    /// </summary>
    public const int PageSize = 100;

    /// <summary>
    /// How many paths the delete pulls out of the store at a time. The user may have condemned a million
    /// files, and the point of a batch is that no step of the deletion ever holds more than this many.
    /// </summary>
    private const int DeleteBatchSize = 200;

    /// <summary>
    /// How many failures are listed. A drive whose permissions are a mess can refuse thousands, and the
    /// thousandth tells the user nothing the tenth did not — the count is the finding, the list is only
    /// the evidence. The same bargain the scan strikes with its unreadable files.
    /// </summary>
    private const int MaxFailuresListed = 100;

    private readonly IScanResultStore _resultStore;
    private readonly IFileRecycler _fileRecycler;
    private readonly IFileOpener _fileOpener;
    private readonly IConfirmationPrompt _confirmationPrompt;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The files the user has unticked, by path — the exceptions they have carved out of the deletion.
    /// It lives here and not on the rows because the rows do not live long enough: a page is rebuilt
    /// from the store every time the user turns to it, so a decision remembered on a row would be
    /// forgotten the moment they paged away from it and back.
    ///
    /// It holds exclusions rather than inclusions, and that is the selection model in one field. See
    /// <see cref="DeleteSuggestedAsync"/>.
    /// </summary>
    private readonly HashSet<string> _sparedPaths = new(StringComparer.OrdinalIgnoreCase);

    private Guid _reportId;
    private ScanSummary _summary = new(0, 0, 0, 0, 0);
    private Recommendation _shown = Recommendation.Review;
    private int _pageIndex;
    private int _pendingDeletion;
    private string _deletionOutcome = string.Empty;

    public ResultsViewModel(
        IScanResultStore resultStore,
        IFileRecycler fileRecycler,
        IFileOpener fileOpener,
        IConfirmationPrompt confirmationPrompt,
        TimeProvider timeProvider)
    {
        _resultStore = resultStore;
        _fileRecycler = fileRecycler;
        _fileOpener = fileOpener;
        _confirmationPrompt = confirmationPrompt;
        _timeProvider = timeProvider;

        ShowNeedsReviewCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Review));
        ShowToDeleteCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Delete));
        ShowToKeepCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Retain));

        NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => HasPreviousPage);

        DeleteSuggestedCommand = new AsyncRelayCommand(DeleteSuggestedAsync, () => CanDelete);
    }

    /// <summary>The page the user is looking at. Never longer than <see cref="PageSize"/>.</summary>
    public ObservableCollection<FileRowViewModel> Rows { get; } = [];

    public ICommand ShowNeedsReviewCommand { get; }

    public ICommand ShowToDeleteCommand { get; }

    public ICommand ShowToKeepCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    /// <summary>Sends every file still suggested for deletion, bar the ones the user unticked, to the recycle bin.</summary>
    public ICommand DeleteSuggestedCommand { get; }

    /// <summary>Which of the three headings is open. The view highlights it; nothing else reads it.</summary>
    public Recommendation Shown
    {
        get => _shown;
        private set => Set(ref _shown, value);
    }

    public bool IsShowingNeedsReview => Shown == Recommendation.Review;

    public bool IsShowingToDelete => Shown == Recommendation.Delete;

    public bool IsShowingToKeep => Shown == Recommendation.Retain;

    public string NeedsReviewHeading => $"Needs review ({_summary.FilesNeedingReview:N0})";

    public string ToDeleteHeading => $"Suggested for deletion ({_summary.FilesRecommendedForDeletion:N0})";

    public string ToKeepHeading => $"Keep ({CountOf(Recommendation.Retain):N0})";

    /// <summary>
    /// How many files the delete button would actually send to the recycle bin: everything still
    /// condemned, less everything the user has spared.
    /// </summary>
    public int DeletableCount => Math.Max(0, _pendingDeletion - _sparedPaths.Count);

    /// <summary>
    /// The button, in the words of the thing it does. Never "Delete selected" — the user is not
    /// deleting a selection, they are acting on a recommendation, and the number is what makes it a
    /// decision rather than a leap.
    /// </summary>
    public string DeleteButtonText => DeletableCount == 1
        ? "Send 1 file to the recycle bin"
        : $"Send {DeletableCount:N0} files to the recycle bin";

    /// <summary>Only under the deletion heading, and only while there is something left to delete.</summary>
    public bool CanDelete => IsShowingToDelete && DeletableCount > 0;

    /// <summary>What happened last time the user pressed it: "12 files sent to the recycle bin."</summary>
    public string DeletionOutcome
    {
        get => _deletionOutcome;
        private set
        {
            Set(ref _deletionOutcome, value);
            Notify(nameof(HasDeletionOutcome));
        }
    }

    public bool HasDeletionOutcome => DeletionOutcome.Length > 0;

    /// <summary>
    /// The files that would not go — locked, already gone, access denied. Counted and listed exactly as
    /// the scan reports the files it could not read, and for the same reason: a delete that silently
    /// skipped four hundred files has told the user a comfortable lie about the state of their drive.
    /// </summary>
    public IReadOnlyList<RecycleFailure> DeletionFailures { get; private set; } = [];

    public bool HasDeletionFailures => DeletionFailures.Count > 0;

    /// <summary>"Showing 1–100 of 482 files."</summary>
    public string PageDescription
    {
        get
        {
            int total = CountOf(Shown);

            if (total == 0)
            {
                return EmptyDescription;
            }

            int first = (_pageIndex * PageSize) + 1;
            int last = Math.Min(first + PageSize - 1, total);

            return $"Showing {first:N0}–{last:N0} of {PlainLanguage.Files(total)}.";
        }
    }

    public bool HasPreviousPage => _pageIndex > 0;

    public bool HasNextPage => (_pageIndex + 1) * PageSize < CountOf(Shown);

    /// <summary>
    /// Opens a report's detail list, on whichever heading the user most needs to see: the files that
    /// need a decision if there are any, and only then the ones the tool is confident about.
    /// </summary>
    public async Task LoadAsync(Guid reportId, ScanSummary summary)
    {
        _reportId = reportId;
        _summary = summary;

        // A fresh report, so the exceptions carved out of the last one mean nothing here — and neither
        // does what happened when the last one was acted on.
        _sparedPaths.Clear();
        DeletionOutcome = string.Empty;
        DeletionFailures = [];

        Notify(nameof(HasDeletionFailures));
        Notify(nameof(DeletionFailures));

        await RefreshPendingDeletionAsync();
        await ShowAsync(FirstHeadingWorthOpening(summary));
    }

    /// <summary>Opens one of the three headings, at its first page.</summary>
    public async Task ShowAsync(Recommendation recommendation)
    {
        Shown = recommendation;

        Notify(nameof(IsShowingNeedsReview));
        Notify(nameof(IsShowingToDelete));
        Notify(nameof(IsShowingToKeep));
        Notify(nameof(CanDelete));

        await GoToPageAsync(0);
    }

    /// <summary>The next page. Public, like the scan itself, so a test can await it — a command cannot be.</summary>
    public Task NextPageAsync() => GoToPageAsync(_pageIndex + 1);

    /// <summary>The previous page.</summary>
    public Task PreviousPageAsync() => GoToPageAsync(_pageIndex - 1);

    /// <summary>
    /// Sends the files still suggested for deletion to the recycle bin — all of them, except the ones the
    /// user has unticked. Public so a test can await it; a command cannot be.
    /// </summary>
    /// <remarks>
    /// <b>The selection model, and what it rejects.</b> Every deletable row arrives ticked, and unticking
    /// one <i>spares</i> it. The alternative — an empty selection the user fills in by ticking — was
    /// rejected because it makes the default action nothing at all: a scan that condemns ten thousand
    /// files would demand the user page through a hundred screens ticking boxes to say the one thing they
    /// came here to say, which is "yes, do what you suggested". Supporting both models was rejected too;
    /// two ways to select on one list is a settings screen wearing a checkbox.
    ///
    /// So the recommendation is the selection, and the user's job is to confirm it or carve exceptions
    /// out of it. That is the same bargain the whole tool is built on — DataSentry recommends, the user
    /// decides — and here it is finally cashed.
    ///
    /// <b>The paths come from the store, not from the screen.</b> The rows are one page of a hundred, and
    /// the user is deleting all twelve thousand: the screen simply does not know most of what is about to
    /// go. Asking the store also means the deletable set is filtered by the recommendation in SQL, so a
    /// file that needs review is not merely un-tickable on screen — it is unreachable from here.
    /// </remarks>
    public async Task DeleteSuggestedAsync()
    {
        if (!CanDelete)
        {
            return;
        }

        int deletableCount = DeletableCount;

        bool confirmed = await _confirmationPrompt.ConfirmAsync(
            deletableCount == 1
                ? "Send 1 file to the recycle bin?"
                : $"Send {deletableCount:N0} files to the recycle bin?",
            "You can get them back from the recycle bin if you change your mind. " +
            "Files that need review are never deleted this way.");

        if (!confirmed)
        {
            return;
        }

        await RecycleAsync();
    }

    private async Task RecycleAsync()
    {
        DateTimeOffset recycledUtc = _timeProvider.GetUtcNow();

        var failures = new List<RecycleFailure>();
        var recycledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int recycledCount = 0;

        // The cursor skips over the files that stay behind. A spared file and a file that would not go
        // are both still pending deletion when this loop comes round again, so without this the next
        // page would hand back the same paths for ever.
        int skip = 0;

        while (true)
        {
            IReadOnlyList<string> pending = await _resultStore.GetPathsPendingDeletionAsync(
                _reportId,
                skip,
                DeleteBatchSize);

            if (pending.Count == 0)
            {
                break;
            }

            var wentToTheBin = new List<string>(pending.Count);

            foreach (string filePath in pending)
            {
                if (_sparedPaths.Contains(filePath))
                {
                    continue;
                }

                RecycleFailure? failure = await _fileRecycler.RecycleAsync(filePath);

                // One bad file never takes the batch down with it. It is counted, it is named, and the
                // loop moves on to the next — exactly what the scan does with a file it cannot read.
                if (failure is not null)
                {
                    failures.Add(failure);
                    continue;
                }

                wentToTheBin.Add(filePath);
            }

            await _resultStore.MarkRecycledAsync(_reportId, wentToTheBin, recycledUtc);

            recycledCount += wentToTheBin.Count;
            recycledPaths.UnionWith(wentToTheBin);

            skip += pending.Count - wentToTheBin.Count;
        }

        DeletionFailures = [.. failures.Take(MaxFailuresListed)];
        DeletionOutcome = Describe(recycledCount, failures.Count);

        Notify(nameof(DeletionFailures));
        Notify(nameof(HasDeletionFailures));

        // The rows on screen are stale the instant the file leaves — say so on the page the user is
        // looking at, without making them turn away and back to see it.
        foreach (FileRowViewModel row in Rows.Where(row => recycledPaths.Contains(row.FilePath)))
        {
            row.MarkRecycled();
        }

        await RefreshPendingDeletionAsync();
    }

    /// <summary>"12 files sent to the recycle bin. 2 could not be deleted."</summary>
    private static string Describe(int recycledCount, int failureCount)
    {
        string sent = recycledCount == 1
            ? "1 file sent to the recycle bin."
            : $"{PlainLanguage.Files(recycledCount)} sent to the recycle bin.";

        if (failureCount == 0)
        {
            return sent;
        }

        string refused = failureCount == 1
            ? "1 file could not be deleted."
            : $"{failureCount:N0} files could not be deleted.";

        return $"{sent} {refused}";
    }

    private async Task RefreshPendingDeletionAsync()
    {
        // A spared file is never deleted, so it is still pending deletion when this comes back — which
        // is what keeps the count honest across repeated deletes: the spared set is always a subset of
        // what the store still calls pending, and never double-subtracts a file that has already gone.
        _pendingDeletion = await _resultStore.CountPendingDeletionAsync(_reportId);

        NotifyDeletability();
    }

    private void NotifyDeletability()
    {
        Notify(nameof(DeletableCount));
        Notify(nameof(DeleteButtonText));
        Notify(nameof(CanDelete));
    }

    private async Task GoToPageAsync(int pageIndex)
    {
        _pageIndex = pageIndex;

        IReadOnlyList<FileScanResult> page = await _resultStore.GetResultsPageAsync(
            _reportId,
            Shown,
            pageIndex * PageSize,
            PageSize);

        foreach (FileRowViewModel row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();

        foreach (FileScanResult result in page)
        {
            var row = new FileRowViewModel(result, _fileOpener)
            {
                // The user's exceptions outlive the rows that carried them: a file unticked on page one
                // is still unticked when they page away and come back.
                IsSelectedForDeletion = result.CanBeRecycled && !_sparedPaths.Contains(result.FilePath)
            };

            row.PropertyChanged += OnRowChanged;

            Rows.Add(row);
        }

        Notify(nameof(PageDescription));
        Notify(nameof(HasPreviousPage));
        Notify(nameof(HasNextPage));

        // The headings carry counts, and a freshly loaded report has new ones.
        Notify(nameof(NeedsReviewHeading));
        Notify(nameof(ToDeleteHeading));
        Notify(nameof(ToKeepHeading));
    }

    /// <summary>A row was ticked or unticked, which is the only way the spared set ever changes.</summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(FileRowViewModel.IsSelectedForDeletion) ||
            sender is not FileRowViewModel row ||
            !row.CanBeDeleted)
        {
            return;
        }

        if (row.IsSelectedForDeletion)
        {
            _sparedPaths.Remove(row.FilePath);
        }
        else
        {
            _sparedPaths.Add(row.FilePath);
        }

        NotifyDeletability();
    }

    private string EmptyDescription => Shown switch
    {
        Recommendation.Review => "Nothing here needs a decision from you.",
        Recommendation.Delete => "Nothing here is worth deleting.",
        _ => "Nothing here."
    };

    private int CountOf(Recommendation recommendation) => recommendation switch
    {
        Recommendation.Review => _summary.FilesNeedingReview,
        Recommendation.Delete => _summary.FilesRecommendedForDeletion,

        // Everything the scan judged and did not condemn or flag. Counted rather than stored, because
        // the summary is the headline and a third count adds nothing to it that subtraction does not.
        _ => _summary.FilesScanned - _summary.FilesNeedingReview - _summary.FilesRecommendedForDeletion
    };

    private static Recommendation FirstHeadingWorthOpening(ScanSummary summary)
    {
        if (summary.FilesNeedingReview > 0)
        {
            return Recommendation.Review;
        }

        return summary.FilesRecommendedForDeletion > 0 ? Recommendation.Delete : Recommendation.Retain;
    }
}
