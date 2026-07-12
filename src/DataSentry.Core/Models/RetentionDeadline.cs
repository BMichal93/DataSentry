namespace DataSentry.Core.Models;

/// <summary>
/// Where a document holding personal data stands against the typical legal retention period.
/// Meaningless for a file with no findings — clean files may sit forever; only personal data
/// has a clock on it.
/// </summary>
public enum RetentionDeadline
{
    /// <summary>Comfortably inside the retention period, or the file holds no personal data.</summary>
    None,

    /// <summary>Inside the warning window before the deadline. Worth a look before it breaches.</summary>
    Approaching,

    /// <summary>Older than the typical retention period. The legal basis for keeping it may be gone.</summary>
    Breached
}
