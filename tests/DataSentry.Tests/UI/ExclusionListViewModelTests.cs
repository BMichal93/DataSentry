using System.Linq;
using System.Threading.Tasks;
using DataSentry.Tests.Fakes;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The exclusion list, exercised without a window and without a disk. It starts from whatever the
/// composition root hands it and is edited from there, so a test only ever needs to hand it a plain
/// list of strings.
/// </summary>
[TestFixture]
public class ExclusionListViewModelTests
{
    [Test]
    public void Folders_OnConstruction_ShowsExactlyTheDefaultsHandedIn()
    {
        var viewModel = new ExclusionListViewModel(
            ["C:/Windows", "C:/Program Files"],
            new FakeFolderPicker(null));

        Assert.That(
            viewModel.Folders.Select(folder => folder.Path),
            Is.EqualTo(new[] { "C:/Windows", "C:/Program Files" }));
    }

    [Test]
    public void ExcludedPaths_ReflectsWhateverIsCurrentlyOnTheList()
    {
        var viewModel = new ExclusionListViewModel(["C:/Windows"], new FakeFolderPicker(null));

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
    }

    [Test]
    public async Task AddFolderAsync_AFolderThePickerReturns_IsAddedToTheList()
    {
        var viewModel = new ExclusionListViewModel(["C:/Windows"], new FakeFolderPicker("C:/Archive"));

        await viewModel.AddFolderAsync();

        Assert.That(
            viewModel.ExcludedPaths,
            Is.EqualTo(new[] { "C:/Windows", "C:/Archive" }));
    }

    [Test]
    public async Task AddFolderAsync_TheUserClosesThePickerWithoutChoosing_AddsNothing()
    {
        var viewModel = new ExclusionListViewModel(["C:/Windows"], new FakeFolderPicker(null));

        await viewModel.AddFolderAsync();

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
    }

    [Test]
    public async Task AddFolderAsync_AFolderAlreadyOnTheList_IsNotAddedTwice()
    {
        var viewModel = new ExclusionListViewModel(["C:/Windows"], new FakeFolderPicker("C:/Windows"));

        await viewModel.AddFolderAsync();

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
    }

    [Test]
    public void RemoveCommand_OnAFolderRow_TakesItOffTheList()
    {
        var viewModel = new ExclusionListViewModel(
            ["C:/Windows", "C:/Program Files"],
            new FakeFolderPicker(null));

        viewModel.Folders.Single(folder => folder.Path == "C:/Windows").RemoveCommand.Execute(null);

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Program Files" }));
    }

    [Test]
    public void TogglePanelCommand_OpensThePanelAndPutsItAwayAgain()
    {
        var viewModel = new ExclusionListViewModel([], new FakeFolderPicker(null));

        Assert.That(viewModel.IsPanelOpen, Is.False);

        viewModel.TogglePanelCommand.Execute(null);
        Assert.That(viewModel.IsPanelOpen, Is.True);

        viewModel.TogglePanelCommand.Execute(null);
        Assert.That(viewModel.IsPanelOpen, Is.False);
    }
}
