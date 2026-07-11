using System.Threading.Tasks;

namespace DataSentry.UI.FileActions;

/// <summary>A file that could not be sent to the recycle bin, and why — in words the user can act on.</summary>
public sealed record RecycleFailure(string FilePath, string Reason);

/// <summary>
/// Sends one file to the recycle bin. Returns null when it went, or the reason it could not.
/// </summary>
/// <remarks>
/// <b>The same line <see cref="Dialogs.IFolderPicker"/> and <see cref="Scheduling.IScanScheduler"/>
/// draw, drawn again where it matters most.</b> The recycle bin is a Windows shell facility, and a view
/// model that reached for it directly could not be tested without deleting somebody's actual files —
/// so the one class in DataSentry that can destroy anything is the one class the tests replace with a
/// fake. The view model asks for a file to go; only the composition root knows what "the recycle bin"
/// really is.
///
/// It reports failure by returning it rather than throwing, because failure here is ordinary and
/// expected: on a real drive some files are locked, some are already gone, and some belong to someone
/// else. The scan treats an unreadable file as a finding to be counted and reported rather than an
/// emergency, and a batch delete owes the user the same — one bad file must never take the other
/// eleven down with it.
/// </remarks>
public interface IFileRecycler
{
    Task<RecycleFailure?> RecycleAsync(string filePath);
}
