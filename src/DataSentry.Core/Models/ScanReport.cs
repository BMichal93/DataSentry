using System.Collections.Generic;

namespace DataSentry.Core.Models;

/// <summary>
/// One scan of one directory tree. Carries the metadata and the summary; the per-file results
/// are streamed separately, because a tree can hold millions of them.
/// </summary>
public sealed record ScanReport(
    Guid Id,
    string RootPath,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    ScanSummary Summary,
    IReadOnlyList<ScanError> Errors);
