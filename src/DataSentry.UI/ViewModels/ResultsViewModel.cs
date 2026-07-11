using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The detail list: one page of files at a time, all carrying the same recommendation.
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
/// </remarks>
public sealed class ResultsViewModel : ObservableObject
{
    /// <summary>
    /// How many files are on a page. More than a screenful, so scrolling feels continuous; nowhere near
    /// enough to be worth worrying about the memory.
    /// </summary>
    public const int PageSize = 100;

    private readonly IScanResultStore _resultStore;

    private Guid _reportId;
    private ScanSummary _summary = new(0, 0, 0, 0, 0);
    private Recommendation _shown = Recommendation.Review;
    private int _pageIndex;

    public ResultsViewModel(IScanResultStore resultStore)
    {
        _resultStore = resultStore;

        ShowNeedsReviewCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Review));
        ShowToDeleteCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Delete));
        ShowToKeepCommand = new AsyncRelayCommand(() => ShowAsync(Recommendation.Retain));

        NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => HasPreviousPage);
    }

    /// <summary>The page the user is looking at. Never longer than <see cref="PageSize"/>.</summary>
    public ObservableCollection<FileRowViewModel> Rows { get; } = [];

    public ICommand ShowNeedsReviewCommand { get; }

    public ICommand ShowToDeleteCommand { get; }

    public ICommand ShowToKeepCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

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

        await ShowAsync(FirstHeadingWorthOpening(summary));
    }

    /// <summary>Opens one of the three headings, at its first page.</summary>
    public async Task ShowAsync(Recommendation recommendation)
    {
        Shown = recommendation;

        Notify(nameof(IsShowingNeedsReview));
        Notify(nameof(IsShowingToDelete));
        Notify(nameof(IsShowingToKeep));

        await GoToPageAsync(0);
    }

    /// <summary>The next page. Public, like the scan itself, so a test can await it — a command cannot be.</summary>
    public Task NextPageAsync() => GoToPageAsync(_pageIndex + 1);

    /// <summary>The previous page.</summary>
    public Task PreviousPageAsync() => GoToPageAsync(_pageIndex - 1);

    private async Task GoToPageAsync(int pageIndex)
    {
        _pageIndex = pageIndex;

        IReadOnlyList<FileScanResult> page = await _resultStore.GetResultsPageAsync(
            _reportId,
            Shown,
            pageIndex * PageSize,
            PageSize);

        Rows.Clear();

        foreach (FileScanResult result in page)
        {
            Rows.Add(new FileRowViewModel(result));
        }

        Notify(nameof(PageDescription));
        Notify(nameof(HasPreviousPage));
        Notify(nameof(HasNextPage));

        // The headings carry counts, and a freshly loaded report has new ones.
        Notify(nameof(NeedsReviewHeading));
        Notify(nameof(ToDeleteHeading));
        Notify(nameof(ToKeepHeading));
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
