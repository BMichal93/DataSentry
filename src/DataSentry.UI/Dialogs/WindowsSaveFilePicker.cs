using System.Threading.Tasks;
using Microsoft.Win32;

namespace DataSentry.UI.Dialogs;

/// <summary>The Windows save-file dialog, exactly as narrowly wrapped as <see cref="WindowsFolderPicker"/>.</summary>
public sealed class WindowsSaveFilePicker : ISaveFilePicker
{
    public Task<string?> PickSaveFileAsync(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export scan report",
            FileName = suggestedFileName,
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = ".csv"
        };

        // Synchronous, for the reason WindowsFolderPicker's dialog call is: a modal dialog does not
        // return until the user has answered, and the Task is here only so the interface can also be
        // satisfied by a fake that never has to wait.
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}
