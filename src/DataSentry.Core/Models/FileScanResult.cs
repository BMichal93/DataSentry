using System.Collections.Generic;

namespace DataSentry.Core.Models;

/// <summary>One file, classified.</summary>
/// <param name="Reason">Why, in plain language: "Not opened in 3 years", not a predicate.</param>
public sealed record FileScanResult(
    string FilePath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset LastAccessedUtc,
    Recommendation Recommendation,
    RiskLevel RiskLevel,
    string Reason,
    IReadOnlyList<PiiFinding> Findings);
