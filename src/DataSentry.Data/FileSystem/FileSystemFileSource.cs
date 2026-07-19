using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;

namespace DataSentry.Data.FileSystem;

/// <summary>Walks a real directory tree.</summary>
public sealed class FileSystemFileSource : IFileSource
{
    public async IAsyncEnumerable<FileMetadata> EnumerateFilesAsync(
        ScanScope scope,
        Action<ScanError> reportError,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The tree is walked with an explicit stack rather than a recursive enumeration, because both
        // built-in options are wrong here: recursion throws on the first denied folder and abandons the
        // rest of the scan, and IgnoreInaccessible swallows it so the user is never told. A denied folder
        // is a finding — it is reported, and the walk carries on into its siblings.
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(scope.RootPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string currentDirectory = pendingDirectories.Pop();

            if (scope.Excludes(currentDirectory))
            {
                continue;
            }

            DirectoryContents contents = await Task.Run(
                () => ReadDirectory(currentDirectory, reportError),
                cancellationToken);

            foreach (string subdirectory in contents.Subdirectories)
            {
                pendingDirectories.Push(subdirectory);
            }

            foreach (FileMetadata file in contents.Files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// One directory's worth of entries, read on a thread pool thread. The file system APIs are
    /// synchronous and blocking, and a scan must never be the reason the window stops repainting.
    /// </summary>
    private static DirectoryContents ReadDirectory(string directoryPath, Action<ScanError> reportError)
    {
        FileSystemInfo[] entries;

        try
        {
            // Read through the \\?\ form so a folder nested past 260 characters is walked rather than
            // reported as too long. What comes back carries the prefix, and is stripped straight off
            // below, so nothing above the file system ever meets it.
            entries = new DirectoryInfo(ExtendedLengthPath.ToFileSystem(directoryPath)).GetFileSystemInfos();
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            reportError(new ScanError(directoryPath, Describe(exception)));
            return DirectoryContents.Empty;
        }

        var subdirectories = new List<string>();
        var files = new List<FileMetadata>();

        foreach (FileSystemInfo entry in entries)
        {
            // A reparse point is a link to somewhere else in the tree. Following one risks walking the
            // same files twice, or walking in a circle forever.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            if (entry is DirectoryInfo subdirectory)
            {
                subdirectories.Add(ExtendedLengthPath.ToDisplay(subdirectory.FullName));
                continue;
            }

            if (entry is not FileInfo file)
            {
                continue;
            }

            try
            {
                files.Add(ToMetadata(file));
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                reportError(new ScanError(file.FullName, Describe(exception)));
            }
        }

        return new DirectoryContents(subdirectories, files);
    }

    private static FileMetadata ToMetadata(FileInfo file) =>
        new(
            // The path as the user knows it — the \\?\ the file was reached through is stripped, so it
            // never reaches a scan result, the database, or the screen.
            ExtendedLengthPath.ToDisplay(file.FullName),
            file.Length,
            new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            new DateTimeOffset(file.LastAccessTimeUtc, TimeSpan.Zero));

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    /// <summary>Plain language, because this ends up in front of the user.</summary>
    private static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Access denied",
        DirectoryNotFoundException => "Folder not found",
        FileNotFoundException => "File no longer exists",
        PathTooLongException => "Path is too long to read",
        _ => exception.Message
    };

    private sealed record DirectoryContents(
        IReadOnlyList<string> Subdirectories,
        IReadOnlyList<FileMetadata> Files)
    {
        public static DirectoryContents Empty { get; } = new([], []);
    }
}
