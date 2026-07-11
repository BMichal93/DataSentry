using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace DataSentry.UI.FileActions;

/// <summary>
/// The Windows recycle bin. All of DataSentry's knowledge of how a file is destroyed is in these few
/// lines, and nothing upstream of the composition root knows they exist.
/// </summary>
/// <remarks>
/// <b>The recycle bin, and never <see cref="File.Delete"/>.</b> DataSentry recommends; the user decides
/// — and a user who decides wrong deserves to be able to change their mind. A permanent delete of a file
/// the tool merely guessed was junk turns a wrong guess into a lost file, which is the one outcome that
/// would make the tool worse than not running it at all. So the destructive action is reversible, and
/// the reversal is one the user already knows how to perform.
///
/// The recycle bin has no clean BCL API — <see cref="File.Delete"/> does not offer one, because the bin
/// is a shell facility rather than a file-system one. <c>Microsoft.VisualBasic.FileIO.FileSystem</c> is
/// the in-box wrapper over the shell call that does, and it ships in the shared framework: no NuGet
/// package, nothing to install, and the awkward namespace stops at this file.
/// </remarks>
public sealed class RecycleBinFileRecycler : IFileRecycler
{
    public Task<RecycleFailure?> RecycleAsync(string filePath) => Task.Run(() => Recycle(filePath));

    /// <remarks>
    /// Run on a background thread by the caller above, because the shell call is synchronous and a user
    /// deleting ten thousand files should not watch the window turn white while it happens.
    /// </remarks>
    private static RecycleFailure? Recycle(string filePath)
    {
        // Asked before the shell is, because a file that is already gone is the most ordinary failure
        // there is — the user deleted it themselves last week — and it deserves a sentence rather than
        // an exception. The check is not a guarantee that the delete will succeed, and is not meant to
        // be one: something can still take the file in the moment between these two lines, and the
        // catch below is what actually keeps that from ending the batch.
        if (!File.Exists(filePath))
        {
            return new RecycleFailure(filePath, "It is no longer there — something has already deleted it.");
        }

        try
        {
            // OnlyErrorDialogs: no progress bar, and no "are you sure?" from Windows. DataSentry has
            // already asked that question itself, in its own words, with the real number in it.
            FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

            return null;
        }
        catch (OperationCanceledException)
        {
            return new RecycleFailure(filePath, "Windows would not delete it.");
        }
        catch (UnauthorizedAccessException)
        {
            return new RecycleFailure(filePath, "You do not have permission to delete it.");
        }
        catch (IOException)
        {
            return new RecycleFailure(filePath, "It is open in another program.");
        }
    }
}
