using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// A directory tree that never existed. Files are handed out in order, denied folders are reported
/// exactly as the real walker reports them, and nothing touches a disk.
/// </summary>
internal sealed class FakeFileSource : IFileSource
{
    private readonly IReadOnlyList<FileMetadata> _files;
    private readonly IReadOnlyList<ScanError> _walkErrors;
    private readonly Action<FileMetadata>? _onFileEnumerated;
    private readonly Action<ScanScope>? _onScopeReceived;

    public FakeFileSource(
        IReadOnlyList<FileMetadata> files,
        IReadOnlyList<ScanError>? walkErrors = null,
        Action<FileMetadata>? onFileEnumerated = null,
        Action<ScanScope>? onScopeReceived = null)
    {
        _files = files;
        _walkErrors = walkErrors ?? [];
        _onFileEnumerated = onFileEnumerated;
        _onScopeReceived = onScopeReceived;
    }

    public async IAsyncEnumerable<FileMetadata> EnumerateFilesAsync(
        ScanScope scope,
        Action<ScanError> reportError,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _onScopeReceived?.Invoke(scope);

        foreach (ScanError walkError in _walkErrors)
        {
            reportError(walkError);
        }

        foreach (FileMetadata file in _files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _onFileEnumerated?.Invoke(file);

            yield return file;
        }

        await Task.CompletedTask;
    }
}
