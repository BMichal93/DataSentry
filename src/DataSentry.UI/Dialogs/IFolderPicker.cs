using System.Threading.Tasks;

namespace DataSentry.UI.Dialogs;

/// <summary>
/// Asks the user for a folder. Null if they closed the dialog without choosing one.
/// </summary>
/// <remarks>
/// <b>This interface is the line between the view model and Windows.</b> A folder picker is a modal
/// dialog owned by a window, and a view model that opened one directly could no longer be tested
/// without standing up that window — the very thing the layering exists to prevent. So the view model
/// asks for a folder and never learns where the answer came from, and the composition root, which is
/// already the one place allowed to know such things, hands it the implementation that knows about
/// Windows.
///
/// It lives in the UI project rather than in Core because choosing a folder is a thing the presentation
/// layer does. Core has no user to ask.
/// </remarks>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync();
}
