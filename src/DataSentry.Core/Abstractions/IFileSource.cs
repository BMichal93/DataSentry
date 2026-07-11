using System.Collections.Generic;
using System.Threading;
using DataSentry.Core.Models;

namespace DataSentry.Core.Abstractions;

/// <summary>
/// Where the files come from. Core defines the contract and knows nothing about drives, shares
/// or paths — which is what lets every rule be tested without touching a disk.
/// </summary>
public interface IFileSource
{
    /// <summary>
    /// Walks the tree in <paramref name="scope"/> and yields every file it can read, skipping the
    /// folders the scope excludes.
    /// </summary>
    /// <remarks>
    /// Streamed, because a tree can hold millions of files and none of them belong in memory at once.
    /// Unreadable files and folders — denied, locked, path too long — are handed to
    /// <paramref name="reportError"/> and skipped: one bad file must never abort a scan, and an
    /// exception thrown mid-stream would do exactly that.
    /// </remarks>
    IAsyncEnumerable<FileMetadata> EnumerateFilesAsync(
        ScanScope scope,
        Action<ScanError> reportError,
        CancellationToken cancellationToken = default);
}
