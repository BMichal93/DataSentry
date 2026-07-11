using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// The text inside the files that never existed. A path mapped to null holds no text to sample — a
/// JPEG; a path listed as unreadable throws, which is what the real reader does when a file is locked
/// or denied, and what the engine has to survive.
/// </summary>
/// <remarks>
/// It also counts every hash it is asked for, because for the duplicate sweep the hashes it *never*
/// asked for are the point: hashing is the most expensive thing this tool does, and a test that only
/// checked the answer would not notice a sweep that got the right one by reading the whole tree.
/// </remarks>
internal sealed class FakeFileContentReader : IFileContentReader
{
    private readonly IReadOnlyDictionary<string, string?> _textByPath;
    private readonly IReadOnlyDictionary<string, string> _contentHashByPath;
    private readonly IReadOnlySet<string> _unreadablePaths;

    public FakeFileContentReader(
        IReadOnlyDictionary<string, string?>? textByPath = null,
        IReadOnlySet<string>? unreadablePaths = null,
        IReadOnlyDictionary<string, string>? contentHashByPath = null)
    {
        _textByPath = textByPath ?? new Dictionary<string, string?>();
        _contentHashByPath = contentHashByPath ?? new Dictionary<string, string>();
        _unreadablePaths = unreadablePaths ?? new HashSet<string>();
    }

    /// <summary>Every file whose content was read to the end, in the order it was read.</summary>
    public List<string> HashedPaths { get; } = [];

    public Task<string?> ReadTextSampleAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_unreadablePaths.Contains(filePath))
        {
            throw new IOException($"The process cannot access the file '{filePath}' because it is being used by another process.");
        }

        return Task.FromResult(_textByPath.GetValueOrDefault(filePath));
    }

    public Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HashedPaths.Add(filePath);

        if (_unreadablePaths.Contains(filePath))
        {
            throw new IOException($"The process cannot access the file '{filePath}' because it is being used by another process.");
        }

        // A file the test said nothing about is a file unlike any other, so it is nobody's duplicate.
        // That way a test only has to describe the copies it cares about.
        return Task.FromResult(_contentHashByPath.GetValueOrDefault(filePath, $"unique:{filePath}"));
    }
}
