using System.Threading.Tasks;
using System.Windows.Input;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The shell of the main window: two tabs and the choice between them. Search is where scans happen;
/// Reports is where finished ones are read back. Everything either tab does lives in its own view
/// model — this one only decides which of the two is on screen.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private bool _isReportsTabOpen;

    public MainViewModel(SearchViewModel search, ReportsViewModel reports)
    {
        Search = search;
        Reports = reports;

        ShowSearchCommand = new RelayCommand(() => IsReportsTabOpen = false);

        // Opening the tab rereads the list, because a scan may have finished since the last look —
        // on the Search tab, or headless from the Task Scheduler.
        ShowReportsCommand = new AsyncRelayCommand(ShowReportsAsync);
    }

    public SearchViewModel Search { get; }

    public ReportsViewModel Reports { get; }

    public ICommand ShowSearchCommand { get; }

    public ICommand ShowReportsCommand { get; }

    public bool IsSearchTabOpen => !IsReportsTabOpen;

    public bool IsReportsTabOpen
    {
        get => _isReportsTabOpen;
        private set
        {
            Set(ref _isReportsTabOpen, value);
            Notify(nameof(IsSearchTabOpen));
        }
    }

    /// <summary>Called once at startup by the composition root — the code-behind assigns a DataContext
    /// and does nothing else, so it cannot be the one to call this.</summary>
    public Task LoadAsync() => Search.Schedule.LoadAsync();

    /// <summary>Opens the Reports tab with a list no older than the click that asked for it.</summary>
    public async Task ShowReportsAsync()
    {
        await Reports.LoadAsync();

        IsReportsTabOpen = true;
    }
}
