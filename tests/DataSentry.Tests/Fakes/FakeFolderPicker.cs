using System.Threading.Tasks;
using DataSentry.UI.Dialogs;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// A user who has already made up their mind. This is the whole payoff of <see cref="IFolderPicker"/>:
/// the view model can be asked to pick a folder in a test, with no window anywhere in sight.
/// </summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    private readonly string? _folderPath;

    /// <param name="folderPath">The folder the user chooses, or null if they close the dialog.</param>
    public FakeFolderPicker(string? folderPath = null)
    {
        _folderPath = folderPath;
    }

    public Task<string?> PickFolderAsync() => Task.FromResult(_folderPath);
}
