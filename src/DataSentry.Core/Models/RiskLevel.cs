namespace DataSentry.Core.Models;

/// <summary>
/// How sensitive the findings in a file are. This is an input to the recommendation,
/// not the answer shown to the user.
/// </summary>
public enum RiskLevel
{
    None,
    Low,
    Medium,
    High,
    Critical
}
