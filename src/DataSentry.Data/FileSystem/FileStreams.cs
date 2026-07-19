using System.IO;

namespace DataSentry.Data.FileSystem;

/// <summary>How DataSentry opens a file it intends to read. Every reader in the data layer goes through here.</summary>
internal static class FileStreams
{
    /// <summary>
    /// Opens for reading, sharing with everyone. The interesting files are the ones somebody has open
    /// — a spreadsheet sitting in Excel must still be readable, or the scan misses exactly the data it
    /// was built to find.
    /// </summary>
    public static FileStream OpenRead(string filePath, int bufferSizeBytes) =>
        new(
            // The \\?\ form, so a file nested past 260 characters can still be read for PII and hashed
            // for duplicates — the same reach the walker used to find it.
            ExtendedLengthPath.ToFileSystem(filePath),
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                BufferSize = bufferSizeBytes,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
}
