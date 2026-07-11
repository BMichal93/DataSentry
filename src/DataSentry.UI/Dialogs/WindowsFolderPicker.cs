using System.Threading.Tasks;
using Microsoft.Win32;

namespace DataSentry.UI.Dialogs;

/// <summary>
/// The Windows folder dialog. All of DataSentry's knowledge of it is in these few lines, and nothing
/// upstream of the composition root knows they exist.
/// </summary>
public sealed class WindowsFolderPicker : IFolderPicker
{
    public Task<string?> PickFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to scan",
            Multiselect = false
        };

        // Synchronous, because a modal dialog is: it does not return until the user has answered. The
        // Task is here for the interface, so that a picker which genuinely has to wait — or a fake that
        // does not — can satisfy it without the view model caring which it got.
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }
}
