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
/// The view model, exercised without a window and without a disk. Everything it needs arrives through
/// its constructor, so there is nothing to stand up and nothing to mock away — which is the whole
/// argument for injecting it rather than letting it reach for what it wants.
/// </summary>
[TestFixture]
public class MainViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ScanAsync_FolderOfMixedFiles_SaysWhatIsThereInPlainLanguage()
    {
        MainViewModel viewModel = BuildViewModel(
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

        Assert.That(viewModel.Status, Is.EqualTo("3 files, 3.1 GB reclaimable, 1 file needs review."));
    }

    [Test]
    public async Task ScanAsync_NothingWorthDeleting_StillSaysSo()
    {
        MainViewModel viewModel = BuildViewModel([FileAt("C:/work/report.docx", sizeBytes: 4_096)]);

        viewModel.FolderPath = "C:/work";

        await viewModel.ScanAsync();

        Assert.That(viewModel.Status, Is.EqualTo("1 file, 0 bytes reclaimable, 0 files need review."));
    }

    [Test]
    public async Task ScanAsync_FolderThatDoesNotExist_SaysItCouldNotBeReadRatherThanThatItIsEmpty()
    {
        // The walker reports the missing root as a ScanError and yields no files. That is a different
        // thing from an empty folder, and the user is owed the difference: one is a typo, the other is
        // a clean drive.
        MainViewModel viewModel = BuildViewModel(
            files: [],
            walkErrors: [new ScanError("C:/does-not-exist", "Folder not found")]);

        viewModel.FolderPath = "C:/does-not-exist";

        await viewModel.ScanAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Status, Is.EqualTo("That folder could not be read. Nothing was scanned."));
            Assert.That(viewModel.HasUnreadableFiles, Is.True);
        });
    }

    [Test]
    public async Task ScanAsync_FilesTheScanCouldNotRead_AreSurfacedButNotAsTheHeadline()
    {
        MainViewModel viewModel = BuildViewModel(
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
            Assert.That(viewModel.Status, Does.StartWith("1 file,"));
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

        MainViewModel viewModel = BuildViewModel(
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
        MainViewModel? viewModel = null;

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
        MainViewModel viewModel = BuildViewModel([]);

        Assert.That(viewModel.ScanCommand.CanExecute(null), Is.False);

        viewModel.FolderPath = "C:/work";

        Assert.That(viewModel.ScanCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void FolderPath_WhenItChanges_TellsTheWindowAboutIt()
    {
        MainViewModel viewModel = BuildViewModel([]);

        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.FolderPath = "C:/work";

        Assert.That(changedProperties, Is.EqualTo(new[] { nameof(MainViewModel.FolderPath) }));
    }

    [Test]
    public async Task PickFolderAsync_WhenTheUserChoosesAFolder_PutsItInTheBox()
    {
        MainViewModel viewModel = BuildViewModel([], pickedFolder: "C:/chosen");

        await viewModel.PickFolderAsync();

        Assert.That(viewModel.FolderPath, Is.EqualTo("C:/chosen"));
    }

    [Test]
    public async Task PickFolderAsync_WhenTheUserCancelsTheDialog_LeavesTheBoxAlone()
    {
        MainViewModel viewModel = BuildViewModel([], pickedFolder: null);
        viewModel.FolderPath = "C:/already-typed";

        await viewModel.PickFolderAsync();

        Assert.That(viewModel.FolderPath, Is.EqualTo("C:/already-typed"));
    }

    [Test]
    public async Task ShowDetailCommand_BeforeAScanHasRun_IsNotOffered()
    {
        MainViewModel viewModel = BuildViewModel([FileAt("C:/work/a.txt", sizeBytes: 4_096)]);

        Assert.That(viewModel.ShowDetailCommand.CanExecute(null), Is.False);

        viewModel.FolderPath = "C:/work";
        await viewModel.ScanAsync();

        Assert.That(viewModel.ShowDetailCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task ToggleDetailAsync_ShowsTheFilesAndThenHidesThemAgain()
    {
        MainViewModel viewModel = BuildViewModel([FileAt("C:/work/a.txt", sizeBytes: 4_096)]);
        viewModel.FolderPath = "C:/work";
        await viewModel.ScanAsync();

        await viewModel.ToggleDetailAsync();
        Assert.That(viewModel.IsDetailVisible, Is.True);

        await viewModel.ToggleDetailAsync();
        Assert.That(viewModel.IsDetailVisible, Is.False);
    }

    private static MainViewModel BuildViewModel(
        IReadOnlyList<FileMetadata> files,
        IReadOnlyDictionary<string, string?>? textByPath = null,
        IReadOnlyList<ScanError>? walkErrors = null,
        Action<FileMetadata>? onFileEnumerated = null,
        string? pickedFolder = null)
    {
        var store = new InMemoryScanResultStore();
        var contentReader = new FakeFileContentReader(textByPath);

        var scanEngine = new ScanEngine(
            new FakeFileSource(files, walkErrors, onFileEnumerated),
            contentReader,
            store,
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new DuplicateFileSweep(store, contentReader),
            new FixedTimeProvider(Now));

        return new MainViewModel(scanEngine, new ResultsViewModel(store), new FakeFolderPicker(pickedFolder));
    }

    private static FileMetadata FileAt(string filePath, long sizeBytes)
    {
        DateTimeOffset yesterday = Now.AddDays(-1);

        return new FileMetadata(filePath, sizeBytes, yesterday, yesterday, yesterday);
    }
}
