using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// The verdict on one file once every signal has been weighed: what to do with it, how sensitive it
/// is, and the sentence the user is shown.
/// </summary>
public sealed record FileClassification(
    Recommendation Recommendation,
    RiskLevel RiskLevel,
    string Reason);
