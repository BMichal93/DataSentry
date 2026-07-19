using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.UI.Dialogs;
using DataSentry.UI.Settings;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The folders a scan skips outright — Windows, Program Files, and anywhere the user has added.
/// Visible and editable rather than a silent default: a full-drive scan that quietly steered around
/// folders the user never saw would not be a default anyone could trust, or challenge if it were wrong.
/// </summary>
/// <remarks>
/// The list the user last left is remembered in <c>settings.json</c>: a saved list — even one the user
/// cleared to nothing — wins over the machine defaults, and only a first run, with no settings file
/// yet, falls back to them. Every add and remove is written straight back, so an edit survives the
/// window closing on it.
/// </remarks>
public sealed class ExclusionListViewModel : ObservableObject
{
    private readonly IScanSettingsStore _settingsStore;
    private readonly IFolderPicker _folderPicker;
    private readonly ObservableCollection<ExcludedFolderViewModel> _folders;

    private bool _isPanelOpen;

    public ExclusionListViewModel(
        IReadOnlyList<string> defaultExcludedFolders,
        IScanSettingsStore settingsStore,
        IFolderPicker folderPicker)
    {
        _settingsStore = settingsStore;
        _folderPicker = folderPicker;

        // A saved list wins over the defaults even when it is empty — the user who cleared the list meant
        // to. Only the absence of a settings file, which is a first run, falls back to the machine defaults.
        IReadOnlyList<string> startingFolders = settingsStore.Load()?.ExcludedFolders ?? defaultExcludedFolders;
        _folders = new ObservableCollection<ExcludedFolderViewModel>(startingFolders.Select(ToRow));

        TogglePanelCommand = new RelayCommand(() => IsPanelOpen = !IsPanelOpen);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
    }

    /// <summary>Opens and closes the panel behind its icon. Same idea as the schedule panel next to it.</summary>
    public ICommand TogglePanelCommand { get; }

    public ICommand AddFolderCommand { get; }

    public ReadOnlyObservableCollection<ExcludedFolderViewModel> Folders => new(_folders);

    public bool IsPanelOpen
    {
        get => _isPanelOpen;
        private set => Set(ref _isPanelOpen, value);
    }

    /// <summary>What a scan started right now would skip. Read by <see cref="SearchViewModel.ScanAsync"/>.</summary>
    public IReadOnlyList<string> ExcludedPaths => [.. _folders.Select(folder => folder.Path)];

    /// <summary>
    /// Asks the user for a folder to add to the list. Public, and not buried inside the command, so
    /// that a test can await it — the same reason <see cref="SearchViewModel.ScanAsync"/> is.
    /// </summary>
    public async Task AddFolderAsync()
    {
        string? folderPath = await _folderPicker.PickFolderAsync();

        if (folderPath is null || _folders.Any(folder => string.Equals(folder.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _folders.Add(ToRow(folderPath));
        Save();
    }

    private ExcludedFolderViewModel ToRow(string path) => new(path, RemoveFolder);

    private void RemoveFolder(string path)
    {
        ExcludedFolderViewModel? row = _folders.FirstOrDefault(folder => folder.Path == path);

        if (row is not null)
        {
            _folders.Remove(row);
            Save();
        }
    }

    /// <summary>Writes the list as it stands now, so the edit that just happened outlives the session.</summary>
    private void Save() => _settingsStore.Save(new ScanSettings(ExcludedPaths));
}
