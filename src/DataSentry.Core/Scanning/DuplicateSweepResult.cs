namespace DataSentry.Core.Scanning;

/// <summary>
/// What the duplicate sweep changed, so that the headline can be corrected for it. The sweep runs after
/// the results have been counted, and a copy it condemns is a file the summary last saw as one to keep.
/// </summary>
public sealed record DuplicateSweepResult(int FilesMarkedForDeletion, long ReclaimableBytes);
