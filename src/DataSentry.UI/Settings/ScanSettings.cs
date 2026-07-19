namespace DataSentry.UI.Settings;

/// <summary>
/// The scan options that outlive a session, as they sit in <c>settings.json</c>. Just the exclusion
/// list for now — the folders a scan skips — kept as a record so that a second option one day is a new
/// property, not a rewrite of everything that reads the file.
/// </summary>
public sealed record ScanSettings(IReadOnlyList<string> ExcludedFolders);
