using System.Collections.Generic;
using System.Threading.Tasks;
using DataSentry.UI.FileActions;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// The recycler, without the recycle bin. Records what it was asked to destroy and destroys nothing —
/// which is the only way to test a delete flow without betting somebody's files on the test passing.
/// </summary>
internal sealed class FakeFileRecycler : IFileRecycler
{
    private readonly Dictionary<string, string> _refusalsByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every path handed to the recycler, in the order it was handed over.</summary>
    public List<string> RecycleAttempts { get; } = [];

    /// <summary>The paths that actually went, as opposed to the ones this fake was told to refuse.</summary>
    public List<string> Recycled { get; } = [];

    /// <summary>
    /// Makes this file refuse to be deleted, the way a locked or already-deleted one does on a real
    /// drive — so a test can prove that one bad file does not take the batch down with it.
    /// </summary>
    public void Refuse(string filePath, string reason) => _refusalsByPath[filePath] = reason;

    public Task<RecycleFailure?> RecycleAsync(string filePath)
    {
        RecycleAttempts.Add(filePath);

        if (_refusalsByPath.TryGetValue(filePath, out string? reason))
        {
            return Task.FromResult<RecycleFailure?>(new RecycleFailure(filePath, reason));
        }

        Recycled.Add(filePath);

        return Task.FromResult<RecycleFailure?>(null);
    }
}
