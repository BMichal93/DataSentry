namespace DataSentry.Core.Models;

/// <summary>The single verdict DataSentry produces per file. The user decides; the tool only recommends.</summary>
public enum Recommendation
{
    /// <summary>Junk, temp, stale or duplicate.</summary>
    Delete,

    /// <summary>Actively used, or under a legal retention obligation.</summary>
    Retain,

    /// <summary>Contains likely personal data and needs a human decision.</summary>
    Review
}
