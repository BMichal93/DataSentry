namespace DataSentry.Core.Models;

/// <summary>
/// A file as the scan first meets it: what the file system can tell us without opening it.
/// Cheap signals — name, size, timestamps — come from here, and the rules lean on them so that
/// the expensive ones (reading content, hashing) are only paid for when they earn their keep.
/// </summary>
public sealed record FileMetadata(
    string FilePath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset LastAccessedUtc)
{
    /// <summary>The file name with its extension, e.g. "payroll-2019.xlsx".</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The extension, lowercased and including the dot, e.g. ".xlsx". Empty when there is none.</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
}
