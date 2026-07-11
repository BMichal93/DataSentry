using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The Reports tab: the scans still in the database, newest first, and whichever one the user has
/// opened. Choosing a report shows it exactly as it was shown the day it ran — the headline off the
/// summary the report carries, the files paged out of the store on demand.
/// </summary>
/// <remarks>
/// Reports are purged 30 days after the scan, so this list is the last month of history — the purge
/// bounds the list, not the screen. There is nothing to manage here: no delete button, no archive.
/// The retention rule does the housekeeping, which is the whole reason it exists.
/// </remarks>
public sealed class ReportsViewModel : ObservableObject
{
    private readonly IScanResultStore _resultStore;

    private PastScanViewModel? _selectedReport;
    private string _headline = string.Empty;

    public ReportsViewModel(IScanResultStore resultStore, ResultsViewModel results)
    {
        _resultStore = resultStore;

        Results = results;

        CloseReportCommand = new RelayCommand(CloseReport, () => IsReportOpen);
    }

    /// <summary>The files of the opened report, one page at a time. Its own list, so browsing an old
    /// report never disturbs the search the user may have running on the other tab.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Back to the list — the report stays in it, unchanged, for next time.</summary>
    public ICommand CloseReportCommand { get; }

    public ObservableCollection<PastScanViewModel> Reports { get; } = [];

    public bool HasReports => Reports.Count > 0;

    public bool HasNoReports => Reports.Count == 0;

    /// <summary>The report the user opened, or null while they are looking at the list.</summary>
    public PastScanViewModel? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (value is null)
            {
                CloseReport();
                return;
            }

            // Fire-and-forget is deliberate: a property setter cannot await, and the file page arriving
            // a beat after the headline is exactly how the Search tab behaves too. OpenReportAsync is
            // public so a test can await it instead of racing it.
            _ = OpenReportAsync(value);
        }
    }

    /// <summary>The list is a master-detail without the master: one thing on screen at a time.</summary>
    public bool IsReportOpen => SelectedReport is not null;

    public bool IsListOpen => SelectedReport is null;

    /// <summary>"482 files scanned, 3 suggested for deletion, 7 need review." — the opened report's one line.</summary>
    public string Headline
    {
        get => _headline;
        private set => Set(ref _headline, value);
    }

    /// <summary>
    /// Opens one report: headline first, then its first page of files. The one path every opening
    /// takes, whether the view set <see cref="SelectedReport"/> or a test called this directly.
    /// </summary>
    public async Task OpenReportAsync(PastScanViewModel report)
    {
        ChangeSelection(report);

        Headline = PlainLanguage.Headline(report.Report);

        await Results.LoadAsync(report.Report.Id, report.Report.Summary);
    }

    public void CloseReport() => ChangeSelection(null);

    private void ChangeSelection(PastScanViewModel? report)
    {
        Set(ref _selectedReport, report, nameof(SelectedReport));
        Notify(nameof(IsReportOpen));
        Notify(nameof(IsListOpen));
    }

    /// <summary>
    /// Rereads the list from the store. Called at startup and every time the tab is opened, because a
    /// scan may have finished — on the Search tab, or headless from the Task Scheduler — since the last
    /// look, and a history tab showing yesterday's history is worse than none.
    /// </summary>
    public async Task LoadAsync()
    {
        IReadOnlyList<ScanReport> reports = await _resultStore.ListReportsAsync();

        CloseReport();
        Reports.Clear();

        foreach (ScanReport report in reports)
        {
            Reports.Add(new PastScanViewModel(report));
        }

        Notify(nameof(HasReports));
        Notify(nameof(HasNoReports));
    }
}
