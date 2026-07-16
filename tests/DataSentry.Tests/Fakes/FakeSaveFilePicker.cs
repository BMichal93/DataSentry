using System.Threading.Tasks;
using DataSentry.UI.Dialogs;

namespace DataSentry.Tests.Fakes;

/// <summary>A user who has already chosen where to save, or already closed the dialog without choosing.</summary>
internal sealed class FakeSaveFilePicker : ISaveFilePicker
{
    private readonly string? _destinationPath;

    public FakeSaveFilePicker(string? destinationPath = null)
    {
        _destinationPath = destinationPath;
    }

    public Task<string?> PickSaveFileAsync(string suggestedFileName) => Task.FromResult(_destinationPath);
}
