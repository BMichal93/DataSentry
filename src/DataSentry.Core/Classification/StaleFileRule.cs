using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>Files nobody has touched in years. The timestamps say it; nothing has to be opened.</summary>
public sealed class StaleFileRule : IClassificationRule
{
    public RuleVerdict? Evaluate(FileMetadata file, DateTimeOffset nowUtc) =>
        StalenessPolicy.IsStale(file, nowUtc)
            ? new RuleVerdict(Recommendation.Delete, StalenessPolicy.Describe(file, nowUtc))
            : null;
}
