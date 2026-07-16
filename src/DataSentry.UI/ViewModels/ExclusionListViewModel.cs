using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DataSentry.UI.Dialogs;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The folders a scan skips outright — Windows, Program Files, and anywhere the user has added.
/// Visible and editable rather than a silent default: a full-drive scan that quietly steered around
/// folders the user never saw would not be a default anyone could trust, or challenge if it were wrong.
/// </summary>
/// <remarks>
/// Starts from the machine defaults (Windows, Program Files, and the rest) handed in by the
/// composition root, and is edited from there for the rest of the session. There is no settings file
/// yet to remember an edit past the run it was made in — the list resets to the machine defaults on
/// the next launch.
/// </remarks>
public sealed class ExclusionListViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly ObservableCollection<ExcludedFolderViewModel> _folders;

    private bool _isPanelOpen;

    public ExclusionListViewModel(IReadOnlyList<string> defaultExcludedFolders, IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;
        _folders = new ObservableCollection<ExcludedFolderViewModel>(defaultExcludedFolders.Select(ToRow));

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
    }

    private ExcludedFolderViewModel ToRow(string path) => new(path, RemoveFolder);

    private void RemoveFolder(string path)
    {
        ExcludedFolderViewModel? row = _folders.FirstOrDefault(folder => folder.Path == path);

        if (row is not null)
        {
            _folders.Remove(row);
        }
    }
}
