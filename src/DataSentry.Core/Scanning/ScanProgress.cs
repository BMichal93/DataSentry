namespace DataSentry.Core.Scanning;

/// <summary>
/// How far along a scan is. The two counts are reported separately because a scan does not know how
/// much work it has until it has done it: the walker runs ahead finding files while the classifier
/// works through them, so <see cref="FilesDiscovered"/> is a moving target and not a total. Until the
/// walk finishes, the honest thing to show the user is "1,204 of 3,881 so far", not a percentage
/// that keeps sliding backwards.
/// </summary>
public sealed record ScanProgress(
    int FilesDiscovered,
    int FilesScanned,
    string CurrentFilePath);
