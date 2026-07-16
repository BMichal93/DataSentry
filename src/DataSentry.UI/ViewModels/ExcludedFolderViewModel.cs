using System.Windows.Input;

namespace DataSentry.UI.ViewModels;

/// <summary>One folder on the exclusion list, and the one thing the user can do to it: take it off.</summary>
public sealed class ExcludedFolderViewModel
{
    public ExcludedFolderViewModel(string path, Action<string> remove)
    {
        Path = path;
        RemoveCommand = new RelayCommand(() => remove(path));
    }

    public string Path { get; }

    public ICommand RemoveCommand { get; }
}
