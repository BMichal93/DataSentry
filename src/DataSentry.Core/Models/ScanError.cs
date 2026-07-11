namespace DataSentry.Core.Models;

/// <summary>
/// A file or folder the scan could not read — locked, denied, path too long.
/// Recorded and reported, never a reason to abort the scan.
/// </summary>
public sealed record ScanError(
    string Path,
    string Reason);
