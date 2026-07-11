using System.Collections.Generic;
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

    private static MainViewModel BuildViewModel(
        IReadOnlyList<FileMetadata> files,
        IReadOnlyDictionary<string, string?>? textByPath = null)
    {
        var scanEngine = new ScanEngine(
            new FakeFileSource(files),
            new FakeFileContentReader(textByPath),
            new InMemoryScanResultStore(),
            [new JunkFileRule(), new StaleFileRule()],
            [new IbanDetector()],
            new FixedTimeProvider(Now));

        return new MainViewModel(scanEngine);
    }

    private static FileMetadata FileAt(string filePath, long sizeBytes)
    {
        DateTimeOffset yesterday = Now.AddDays(-1);

        return new FileMetadata(filePath, sizeBytes, yesterday, yesterday, yesterday);
    }
}
