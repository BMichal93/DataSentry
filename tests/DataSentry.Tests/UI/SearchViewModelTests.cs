using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Tests.Fakes;
using DataSentry.UI.ViewModels;

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
        InMemoryScanResultStore? store = null)
    {
        store ??= new InMemoryScanResultStore();
        var contentReader = new FakeFileContentReader(textByPath);

        var scanEngine = new ScanEngine(
            new FakeFileSource(files, walkErrors, onFileEnumerated),
            contentReader,
            store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new DuplicateFileSweep(store, contentReader),
            new FixedTimeProvider(Now));

        var results = new ResultsViewModel(
            store,
            new FakeFileRecycler(),
            new FakeFileOpener(),
            new FakeConfirmationPrompt(answer: false),
            new FixedTimeProvider(Now));

        return new SearchViewModel(
            scanEngine,
            results,
            new ScheduleViewModel(new FakeScanScheduler()),
            new FakeFolderPicker(pickedFolder));
    }

    private static FileMetadata FileAt(string filePath, long sizeBytes)
    {
        DateTimeOffset yesterday = Now.AddDays(-1);

        return new FileMetadata(filePath, sizeBytes, yesterday, yesterday, yesterday);
    }
}
