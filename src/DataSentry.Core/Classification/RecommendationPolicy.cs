using System.Collections.Generic;
using System.Linq;
using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// The one place where a file gets its verdict. Every signal the scan gathered — what the rules made
/// of the name and the timestamps, what the detectors found inside — arrives here, and the first of
/// four questions that applies decides the answer:
///
/// <list type="number">
///   <item>Special-category data (GDPR Art. 9)? Review. Never deleted, under any circumstances.</item>
///   <item>Financial or identity data? Review.</item>
///   <item>Ordinary personal data? Review if the file is also stale — otherwise keep it, and say why.</item>
///   <item>Nothing personal? Then, and only then, the ordinary junk and staleness rules get their say.</item>
/// </list>
///
/// The consequence of that order is the rule the whole tool rests on: <b>a finding overrides a
/// Delete.</b> Personal data is a liability, but it is just as likely to be under a retention
/// obligation — invoices and tax records run five to seven years. Deleting it unasked is the one
/// mistake this tool must never make, so it surfaces it instead.
/// </summary>
public static class RecommendationPolicy
{
    /// <summary>What a file with nothing against it gets.</summary>
    private static readonly RuleVerdict InActiveUse = new(Recommendation.Retain, "In active use");

    public static FileClassification Decide(
        FileMetadata file,
        RuleVerdict? ruleVerdict,
        IReadOnlyList<PiiFinding> findings,
        DateTimeOffset nowUtc)
    {
        RiskLevel risk = RiskOf(findings);

        if (findings.Any(finding => finding.Category == PiiCategory.SpecialCategory))
        {
            return new FileClassification(
                Recommendation.Review,
                risk,
                $"Special category personal data — needs a human decision ({Describe(findings)})");
        }

        if (findings.Any(IsFinancialOrIdentity))
        {
            return new FileClassification(
                Recommendation.Review,
                risk,
                $"Financial or identity data — needs a human decision ({Describe(findings)})");
        }

        if (findings.Count > 0)
        {
            return StalenessPolicy.IsStale(file, nowUtc)
                ? new FileClassification(
                    Recommendation.Review,
                    risk,
                    $"{StalenessPolicy.Describe(file, nowUtc)}, and it holds personal data ({Describe(findings)})")
                : new FileClassification(
                    Recommendation.Retain,
                    risk,
                    $"In use, and it holds personal data ({Describe(findings)})");
        }

        RuleVerdict verdict = ruleVerdict ?? InActiveUse;

        return new FileClassification(verdict.Recommendation, RiskLevel.None, verdict.Reason);
    }

    private static bool IsFinancialOrIdentity(PiiFinding finding) =>
        finding.Category is PiiCategory.Financial or PiiCategory.Identity;

    /// <summary>The file is as sensitive as the worst thing in it.</summary>
    private static RiskLevel RiskOf(IReadOnlyList<PiiFinding> findings) =>
        findings.Count == 0
            ? RiskLevel.None
            : findings.Max(finding => RiskOf(finding.Category));

    private static RiskLevel RiskOf(PiiCategory category) => category switch
    {
        PiiCategory.SpecialCategory => RiskLevel.Critical,
        PiiCategory.Financial => RiskLevel.High,
        PiiCategory.Identity => RiskLevel.High,
        PiiCategory.Contact => RiskLevel.Medium,
        PiiCategory.Network => RiskLevel.Low,
        PiiCategory.Keyword => RiskLevel.Low,
        _ => RiskLevel.Low
    };

    /// <summary>
    /// "3 IBANs, 12 email addresses". The type and the count — never the value, which this model has
    /// nowhere to put and never will.
    /// </summary>
    private static string Describe(IReadOnlyList<PiiFinding> findings) =>
        string.Join(", ", findings
            .OrderByDescending(finding => RiskOf(finding.Category))
            .Select(finding => $"{finding.MatchCount} {Pluralize(finding.DetectorName, finding.MatchCount)}"));

    private static string Pluralize(string detectorName, int matchCount)
    {
        if (matchCount == 1)
        {
            return detectorName;
        }

        // Enough English to keep "1 email address" and "12 email addresses" both reading like English.
        return detectorName.EndsWith('s') || detectorName.EndsWith('x')
            ? $"{detectorName}es"
            : $"{detectorName}s";
    }
}
