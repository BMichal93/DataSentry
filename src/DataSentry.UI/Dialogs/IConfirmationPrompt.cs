using System.Threading.Tasks;

namespace DataSentry.UI.Dialogs;

/// <summary>
/// Asks the user to confirm something that cannot be undone by pressing Escape. True only if they
/// actively said yes.
/// </summary>
/// <remarks>
/// <b>This is the seam the whole branch turns on.</b> DataSentry recommends and the user decides, and
/// this interface is where the deciding happens: every path to the recycle bin runs through a call to
/// <see cref="ConfirmAsync"/> that came back true. A view model that put a message box up itself could
/// not be tested without a window — and the test that matters most here is the one that proves a file
/// is never deleted when the user said no.
///
/// The question is passed in rather than composed here because only the caller knows the number, and
/// the number is the point: "Send 12 files to the recycle bin?" is a question a person can answer.
/// "Are you sure?" is not — it asks the user to confirm something the dialog has not told them.
/// </remarks>
public interface IConfirmationPrompt
{
    Task<bool> ConfirmAsync(string question, string detail);
}
