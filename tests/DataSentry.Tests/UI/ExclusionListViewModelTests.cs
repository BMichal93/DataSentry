using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Tests.Fakes;
using DataSentry.UI.Settings;
using DataSentry.UI.ViewModels;

namespace DataSentry.Tests.UI;

/// <summary>
/// The exclusion list, exercised without a window and without a disk. It starts from what settings.json
/// remembers — or, on a first run, from the machine defaults the composition root hands it — and writes
/// every edit back, so the tests hand it a fake settings store and read what it saved.
/// </summary>
[TestFixture]
public class ExclusionListViewModelTests
{
    [Test]
    public void Folders_OnAFirstRunWithNoSavedSettings_ShowsExactlyTheDefaultsHandedIn()
    {
        ExclusionListViewModel viewModel = Build(
            ["C:/Windows", "C:/Program Files"],
            new FakeFolderPicker(null));

        Assert.That(
            viewModel.Folders.Select(folder => folder.Path),
            Is.EqualTo(new[] { "C:/Windows", "C:/Program Files" }));
    }

    [Test]
    public void Folders_WithASavedList_StartsFromItRatherThanTheDefaults()
    {
        var saved = new FakeScanSettingsStore(new ScanSettings(["D:/Archive", "E:/Backups"]));

        ExclusionListViewModel viewModel = Build(["C:/Windows"], new FakeFolderPicker(null), saved);

        Assert.That(
            viewModel.ExcludedPaths,
            Is.EqualTo(new[] { "D:/Archive", "E:/Backups" }),
            "the list the user last left wins over the machine defaults");
    }

    [Test]
    public void Folders_WithASavedEmptyList_StartsEmptyRatherThanReSeedingTheDefaults()
    {
        // The user who cleared the list to nothing meant to — an empty saved list is an answer, not the
        // absence of one, and re-seeding the defaults would quietly undo their decision every launch.
        var saved = new FakeScanSettingsStore(new ScanSettings([]));

        ExclusionListViewModel viewModel = Build(["C:/Windows", "C:/Program Files"], new FakeFolderPicker(null), saved);

        Assert.That(viewModel.ExcludedPaths, Is.Empty);
    }

    [Test]
    public void ExcludedPaths_ReflectsWhateverIsCurrentlyOnTheList()
    {
        ExclusionListViewModel viewModel = Build(["C:/Windows"], new FakeFolderPicker(null));

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
    }

    [Test]
    public async Task AddFolderAsync_AFolderThePickerReturns_IsAddedToTheListAndSaved()
    {
        var settings = new FakeScanSettingsStore();
        ExclusionListViewModel viewModel = Build(["C:/Windows"], new FakeFolderPicker("C:/Archive"), settings);

        await viewModel.AddFolderAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows", "C:/Archive" }));
            Assert.That(
                settings.Saved.Last().ExcludedFolders,
                Is.EqualTo(new[] { "C:/Windows", "C:/Archive" }),
                "the edit is written back, so it outlives the session");
        });
    }

    [Test]
    public async Task AddFolderAsync_TheUserClosesThePickerWithoutChoosing_AddsNothingAndSavesNothing()
    {
        var settings = new FakeScanSettingsStore();
        ExclusionListViewModel viewModel = Build(["C:/Windows"], new FakeFolderPicker(null), settings);

        await viewModel.AddFolderAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
            Assert.That(settings.Saved, Is.Empty, "nothing changed, so nothing was written");
        });
    }

    [Test]
    public async Task AddFolderAsync_AFolderAlreadyOnTheList_IsNotAddedTwice()
    {
        ExclusionListViewModel viewModel = Build(["C:/Windows"], new FakeFolderPicker("C:/Windows"));

        await viewModel.AddFolderAsync();

        Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Windows" }));
    }

    [Test]
    public void RemoveCommand_OnAFolderRow_TakesItOffTheListAndSaves()
    {
        var settings = new FakeScanSettingsStore();
        ExclusionListViewModel viewModel = Build(
            ["C:/Windows", "C:/Program Files"],
            new FakeFolderPicker(null),
            settings);

        viewModel.Folders.Single(folder => folder.Path == "C:/Windows").RemoveCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ExcludedPaths, Is.EqualTo(new[] { "C:/Program Files" }));
            Assert.That(settings.Saved.Last().ExcludedFolders, Is.EqualTo(new[] { "C:/Program Files" }));
        });
    }

    [Test]
    public void TogglePanelCommand_OpensThePanelAndPutsItAwayAgain()
    {
        ExclusionListViewModel viewModel = Build([], new FakeFolderPicker(null));

        Assert.That(viewModel.IsPanelOpen, Is.False);

        viewModel.TogglePanelCommand.Execute(null);
        Assert.That(viewModel.IsPanelOpen, Is.True);

        viewModel.TogglePanelCommand.Execute(null);
        Assert.That(viewModel.IsPanelOpen, Is.False);
    }

    private static ExclusionListViewModel Build(
        IReadOnlyList<string> defaultExcludedFolders,
        FakeFolderPicker folderPicker,
        FakeScanSettingsStore? settingsStore = null) =>
        new(defaultExcludedFolders, settingsStore ?? new FakeScanSettingsStore(), folderPicker);
}
