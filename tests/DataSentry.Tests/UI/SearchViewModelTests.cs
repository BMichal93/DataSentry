using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Tests.Fakes;
using DataSentry.UI.Reporting;
using DataSentry.UI.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace DataSentry.Tests.UI;

/// <summary>
/// The Search tab, exercised without a window and without a disk. Everything it needs arrives through
/// its constructor, so there is nothing to stand up and nothing to mock away — which is the whole
/// argument for injecting it rather than letting it reach for what it wants.
/// </summary>
[TestFixture]
public class SearchViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ScanAsync_FolderOfMixedFiles_SaysWhatIsThereInPlainLanguage()
    {
        SearchViewModel viewModel = BuildViewModel(
        [
            FileAt("C:/work/report.docx", sizeBytes: 2_000_000_000),
            FileAt("C:/work/export.tmp", sizeBytes: 3_328_599_654),
            FileAt("C:/work/suppliers.csv", sizeBytes: 4_096)
        ],
        textByPath: new Dictionary<string, string?>
        {
            ["C:/work/suppliers.csv"] = "Kowalski,PL61 1090 1014 0000 0712 1981 2874"
        });

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.That(viewModel.Status, Is.EqualTo("3 files scanned, 1 suggested for deletion, 1 needs review."));
    }

    [Test]
    public async Task ScanAsync_NothingWorthDeleting_StillSaysSo()
    {
        SearchViewModel viewModel = BuildViewModel([FileAt("C:/work/report.docx", sizeBytes: 4_096)]);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.That(viewModel.Status, Is.EqualTo("1 file scanned, nothing suggested for deletion, nothing needs review."));
    }

    [Test]
    public async Task ScanAsync_WhenItFinishes_TheFilesAreAlreadyOnScreen()
    {
        // A search that made the user ask to see its results would not be much of a search: the rows
        // are loaded the moment the scan ends, with no toggle standing between the user and them.
        SearchViewModel viewModel = BuildViewModel(
        [
            FileAt("C:/work/export.tmp", sizeBytes: 4_096),
            FileAt("C:/work/report.docx", sizeBytes: 4_096)
        ]);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasFilesToShow, Is.True);
            Assert.That(viewModel.Results.Rows, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ScanAsync_FolderThatDoesNotExist_SaysItCouldNotBeReadRatherThanThatItIsEmpty()
    {
        // The walker reports the missing root as a ScanError and yields no files. That is a different
        // thing from an empty folder, and the user is owed the difference: one is a typo, the other is
        // a clean drive.
        SearchViewModel viewModel = BuildViewModel(
            files: [],
            walkErrors: [new ScanError("C:/does-not-exist", "Folder not found")]);

        viewModel.FolderPath = "C:/does-not-exist";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Status, Is.EqualTo("That folder could not be read. Nothing was scanned."));
            Assert.That(viewModel.HasUnreadableFiles, Is.True);

            // Three filter chips over an empty list would make the answer look unfinished.
            Assert.That(viewModel.HasFilesToShow, Is.False);
        });
    }

    [Test]
    public async Task ScanAsync_FilesTheScanCouldNotRead_AreSurfacedButNotAsTheHeadline()
    {
        SearchViewModel viewModel = BuildViewModel(
            files: [FileAt("C:/work/report.docx", sizeBytes: 4_096)],
            walkErrors:
            [
                new ScanError("C:/work/locked.xlsx", "Access denied"),
                new ScanError("C:/work/secret", "Access denied")
            ]);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            // The headline is still about the files that were judged, not the ones that were not.
            Assert.That(viewModel.Status, Does.StartWith("1 file scanned,"));
            Assert.That(viewModel.HasUnreadableFiles, Is.True);
            Assert.That(viewModel.UnreadableFilesSummary, Is.EqualTo("2 files could not be read, and were not judged."));
            Assert.That(viewModel.UnreadableFiles, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ScanAsync_ManyUnreadableFiles_ListsOnlyTheFirstHundredButCountsThemAll()
    {
        List<ScanError> walkErrors = Enumerable
            .Range(0, 250)
            .Select(index => new ScanError($"C:/work/denied-{index}", "Access denied"))
            .ToList();

        SearchViewModel viewModel = BuildViewModel(
            files: [FileAt("C:/work/report.docx", sizeBytes: 4_096)],
            walkErrors: walkErrors);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.UnreadableFiles, Has.Count.EqualTo(100));
            Assert.That(
                viewModel.UnreadableFilesSummary,
                Is.EqualTo("250 files could not be read, and were not judged. The first 100 are listed."));
        });
    }

    [Test]
    public async Task ScanAsync_Cancelled_StopsAndSaysSoWithoutAResult()
    {
        // A file source that cancels the scan the moment the first file is enumerated. The scan honours
        // the token, throws OperationCanceledException, and the view model turns that into a plain
        // sentence rather than a crash.
        SearchViewModel? viewModel = null;

        viewModel = BuildViewModel(
            files: [FileAt("C:/work/a.txt", sizeBytes: 4_096)],
            onFileEnumerated: _ => viewModel!.CancelScan());

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Status, Is.EqualTo("Scan cancelled. Nothing was changed."));
            Assert.That(viewModel.HasResults, Is.False);
            Assert.That(viewModel.IsScanning, Is.False);
        });
    }

    [Test]
    public void ScanCommand_BeforeAFolderIsNamed_IsNotOfferedToTheUser()
    {
        SearchViewModel viewModel = BuildViewModel([]);

        Assert.That(viewModel.ScanCommand.CanExecute(null), Is.False);

        viewModel.FolderPath = "C:/work";

        Assert.That(viewModel.ScanCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void FolderPath_WhenItChanges_TellsTheWindowAboutIt()
    {
        SearchViewModel viewModel = BuildViewModel([]);

        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.FolderPath = "C:/work";

        Assert.That(changedProperties, Is.EqualTo(new[] { nameof(SearchViewModel.FolderPath) }));
    }

    [Test]
    public async Task PickFolderAsync_WhenTheUserChoosesAFolder_PutsItInTheBox()
    {
        SearchViewModel viewModel = BuildViewModel([], pickedFolder: "C:/chosen");

        await viewModel.PickFolderAsync();

        Assert.That(viewModel.FolderPath, Is.EqualTo("C:/chosen"));
    }

    [Test]
    public async Task PickFolderAsync_WhenTheUserCancelsTheDialog_LeavesTheBoxAlone()
    {
        SearchViewModel viewModel = BuildViewModel([], pickedFolder: null);
        viewModel.FolderPath = "C:/already-typed";

        await viewModel.PickFolderAsync();

        Assert.That(viewModel.FolderPath, Is.EqualTo("C:/already-typed"));
    }

    [Test]
    public async Task ScanAsync_StartTimeStillAhead_WaitsAndSaysWhatWillRunAndWhen()
    {
        // "Scan tonight at 22:00", asked at 09:00. Nothing runs until the clock gets there — and while
        // nothing runs, the screen says what is pending, because a scan the user cannot see is a scan
        // they will not trust.
        FakeTimeProvider clock = MovableClock();
        SearchViewModel viewModel = BuildViewModel(
            [FileAt("C:/work/report.docx", sizeBytes: 4_096)],
            timeProvider: clock);

        viewModel.FolderPath = "C:/work";
        viewModel.StartTimeText = "22:00";

        Task scanning = viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsWaitingToScan, Is.True);
            Assert.That(viewModel.Status, Is.EqualTo("Will scan C:/work today at 22:00. Call it off any time before then."));
            Assert.That(viewModel.HasResults, Is.False);
        });

        clock.Advance(TimeSpan.FromHours(12));

        Assert.That(scanning.IsCompleted, Is.False, "Nothing may run before the chosen time.");

        clock.Advance(TimeSpan.FromHours(1));

        await scanning;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsWaitingToScan, Is.False);
            Assert.That(viewModel.HasResults, Is.True);
            Assert.That(viewModel.Status, Does.StartWith("1 file scanned,"));
        });
    }

    [Test]
    public async Task ScanAsync_StartTimeAlreadyPassedToday_SaysItWillRunTomorrow()
    {
        FakeTimeProvider clock = MovableClock();
        SearchViewModel viewModel = BuildViewModel([], timeProvider: clock);

        viewModel.FolderPath = "C:/work";
        viewModel.StartTimeText = "07:00";

        Task scanning = viewModel.ScanAsync();

        Assert.That(viewModel.Status, Is.EqualTo("Will scan C:/work tomorrow at 07:00. Call it off any time before then."));

        viewModel.CancelScan();
        await scanning;
    }

    [Test]
    public async Task ScanAsync_CalledOffWhileWaiting_NothingRunsAndTheScreenSaysSo()
    {
        FakeTimeProvider clock = MovableClock();
        SearchViewModel viewModel = BuildViewModel(
            [FileAt("C:/work/report.docx", sizeBytes: 4_096)],
            timeProvider: clock);

        viewModel.FolderPath = "C:/work";
        viewModel.StartTimeText = "22:00";

        Task scanning = viewModel.ScanAsync();

        viewModel.CancelScan();
        await scanning;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Status, Is.EqualTo("Scan called off. Nothing was changed."));
            Assert.That(viewModel.IsWaitingToScan, Is.False);
            Assert.That(viewModel.IsScanning, Is.False);
            Assert.That(viewModel.HasResults, Is.False);
        });

        // And the moment coming around later must not resurrect it.
        clock.Advance(TimeSpan.FromDays(1));

        Assert.That(viewModel.HasResults, Is.False);
    }

    [Test]
    public async Task ScanAsync_StartTimeThatIsNotATime_ExplainsAndScansNothing()
    {
        SearchViewModel viewModel = BuildViewModel([FileAt("C:/work/report.docx", sizeBytes: 4_096)]);

        viewModel.FolderPath = "C:/work";
        viewModel.StartTimeText = "half past nine";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Status,
                Is.EqualTo("\"half past nine\" is not a time DataSentry understands. Try 22:00, or leave it empty to scan now."));
            Assert.That(viewModel.HasResults, Is.False);
        });
    }

    [Test]
    public async Task ScanAsync_PathPastedWithQuotesFromExplorer_ScansTheFolderInsideThem()
    {
        // Explorer's "Copy as path" wraps the path in quotes. The quotes are the clipboard's, not the
        // folder's, and the scan has to see through them — and through stray spaces around them.
        var store = new InMemoryScanResultStore();

        SearchViewModel viewModel = BuildViewModel(
            [FileAt(@"C:\work\report.docx", sizeBytes: 4_096)],
            store: store);

        viewModel.FolderPath = " \"C:\\work\" ";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Status,
                Is.EqualTo("1 file scanned, nothing suggested for deletion, nothing needs review."));
            Assert.That(store.CompletedReports.Single().RootPath, Is.EqualTo(@"C:\work"));
        });
    }

    [Test]
    public async Task ScanAsync_PathThatIsNotAFullPath_ExplainsAndScansNothing()
    {
        // A relative path would resolve against wherever the process happens to be standing, which is
        // never the folder the user meant. The box takes full paths only, and says so in its own words.
        SearchViewModel viewModel = BuildViewModel([FileAt(@"C:\work\report.docx", sizeBytes: 4_096)]);

        viewModel.FolderPath = "work";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Status,
                Is.EqualTo("\"work\" is not a full folder path. Try one like C:\\Users\\you\\Documents, or pick the folder with Browse."));
            Assert.That(viewModel.HasResults, Is.False);
        });
    }

    [Test]
    public async Task ScheduleScanAsync_PathPastedWithQuotes_SchedulesTheFolderInsideThem()
    {
        // The schedule outlives the session and is replayed by Windows verbatim, so a quote that slips
        // through here would break every scheduled scan from now on — the folder box is read the same
        // careful way for the schedule as for the scan.
        var scheduler = new FakeScanScheduler();
        SearchViewModel viewModel = BuildViewModel([], scheduler: scheduler);

        viewModel.FolderPath = "\"C:\\work\"";

        await viewModel.ScheduleScanAsync();

        Assert.That(scheduler.Scheduled?.FolderPath, Is.EqualTo(@"C:\work"));
    }

    [Test]
    public async Task ScheduleScanAsync_PathThatIsNotAFullPath_ExplainsAndSchedulesNothing()
    {
        var scheduler = new FakeScanScheduler();
        SearchViewModel viewModel = BuildViewModel([], scheduler: scheduler);

        viewModel.FolderPath = "work";

        await viewModel.ScheduleScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Status,
                Is.EqualTo("\"work\" is not a full folder path. Try one like C:\\Users\\you\\Documents, or pick the folder with Browse."));
            Assert.That(scheduler.Scheduled, Is.Null);
        });
    }

    [Test]
    public async Task ScanAsync_ExcludedFoldersOnTheList_ReachTheScopeTheEngineWalks()
    {
        // The exclusion list is only worth having if a scan actually honours it — this is the seam
        // where a defaults-with-no-caller regression would show up again.
        var exclusions = new ExclusionListViewModel(
            ["C:/Windows", "C:/Program Files"],
            new FakeFolderPicker(null));

        ScanScope? scopeReceived = null;
        SearchViewModel viewModel = BuildViewModel(
            [],
            exclusions: exclusions,
            onScopeReceived: scope => scopeReceived = scope);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.That(scopeReceived?.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows", "C:/Program Files" }));
    }

    [Test]
    public void ToggleSchedulePanel_OpensThePanelAndPutsItAwayAgain()
    {
        // The schedule hides behind the clock icon: closed on arrival, because most visits to this
        // screen are here to scan, and open only for as long as the user wants it in front of them.
        SearchViewModel viewModel = BuildViewModel([]);

        Assert.That(viewModel.IsSchedulePanelOpen, Is.False);

        viewModel.ToggleSchedulePanelCommand.Execute(null);
        Assert.That(viewModel.IsSchedulePanelOpen, Is.True);

        viewModel.ToggleSchedulePanelCommand.Execute(null);
        Assert.That(viewModel.IsSchedulePanelOpen, Is.False);
    }

    private static SearchViewModel BuildViewModel(
        IReadOnlyList<FileMetadata> files,
        IReadOnlyDictionary<string, string?>? textByPath = null,
        IReadOnlyList<ScanError>? walkErrors = null,
        Action<FileMetadata>? onFileEnumerated = null,
        string? pickedFolder = null,
        InMemoryScanResultStore? store = null,
        TimeProvider? timeProvider = null,
        FakeScanScheduler? scheduler = null,
        ExclusionListViewModel? exclusions = null,
        Action<ScanScope>? onScopeReceived = null)
    {
        store ??= new InMemoryScanResultStore();
        timeProvider ??= new FixedTimeProvider(Now);

        var contentReader = new FakeFileContentReader(textByPath);

        var scanEngine = new ScanEngine(
            new FakeFileSource(files, walkErrors, onFileEnumerated, onScopeReceived),
            contentReader,
            store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new DuplicateFileSweep(store, contentReader),
            timeProvider);

        var results = new ResultsViewModel(
            store,
            new FakeFileRecycler(),
            new FakeFileOpener(),
            new FakeConfirmationPrompt(answer: false),
            new FakeSaveFilePicker(),
            new ScanReportExporter(),
            timeProvider);

        return new SearchViewModel(
            scanEngine,
            new DelayedScanStart(timeProvider),
            results,
            new ScheduleViewModel(scheduler ?? new FakeScanScheduler()),
            exclusions ?? new ExclusionListViewModel([], new FakeFolderPicker(null)),
            new FakeFolderPicker(pickedFolder),
            timeProvider);
    }

    /// <summary>A movable clock at 09:00 UTC on a machine whose local time is UTC, for the delayed scans.</summary>
    private static FakeTimeProvider MovableClock()
    {
        var clock = new FakeTimeProvider(Now);

        clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        return clock;
    }

    private static FileMetadata FileAt(string filePath, long sizeBytes)
    {
        DateTimeOffset yesterday = Now.AddDays(-1);

        return new FileMetadata(filePath, sizeBytes, yesterday, yesterday, yesterday);
    }
}
