using System.Threading.Tasks;

namespace DataSentry.UI.Dialogs;

/// <summary>
/// Asks the user where to save a file. Null if they closed the dialog without choosing a location.
/// </summary>
/// <remarks>
/// The same seam <see cref="IFolderPicker"/> is, for the same reason: a view model that opened a save
/// dialog directly could not be tested without a window standing behind it.
/// </remarks>
public interface ISaveFilePicker
{
    /// <param name="suggestedFileName">The name the dialog opens with — the user may change it.</param>
    Task<string?> PickSaveFileAsync(string suggestedFileName);
}
