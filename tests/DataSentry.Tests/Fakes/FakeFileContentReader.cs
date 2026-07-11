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
internal sealed class FakeFileContentReader : IFileContentReader
{
    private readonly IReadOnlyDictionary<string, string?> _textByPath;
    private readonly IReadOnlySet<string> _unreadablePaths;

    public FakeFileContentReader(
        IReadOnlyDictionary<string, string?>? textByPath = null,
        IReadOnlySet<string>? unreadablePaths = null)
    {
        _textByPath = textByPath ?? new Dictionary<string, string?>();
        _unreadablePaths = unreadablePaths ?? new HashSet<string>();
    }

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

    public Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nothing in this branch hashes a file: the duplicate rule is not built yet.");
}
