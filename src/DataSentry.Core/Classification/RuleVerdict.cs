using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// What a rule concluded about a file, and why — in words the user can read.
/// </summary>
/// <param name="Reason">Plain language: "Not opened in 3 years", never a predicate.</param>
public sealed record RuleVerdict(Recommendation Recommendation, string Reason);
