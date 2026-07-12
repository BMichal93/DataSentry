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
/// One clock cuts across all three finding rules: the legal retention period
/// (<see cref="LegalRetentionPolicy"/>). A file whose personal data is approaching or past that
/// deadline is flagged on top of whatever the rules said — and for ordinary personal data the flag
/// does what staleness does, elevating Retain to Review, because data held to the edge of its legal
/// basis is exactly the file a human should look at next.
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

        // The retention clock only ticks for files that hold personal data — a clean file may sit
        // on the drive forever without anyone owing anyone an explanation.
        RetentionDeadline deadline = findings.Count > 0
            ? LegalRetentionPolicy.DeadlineFor(file, nowUtc)
            : RetentionDeadline.None;

        if (findings.Any(finding => finding.Category == PiiCategory.SpecialCategory))
        {
            return new FileClassification(
                Recommendation.Review,
                risk,
                WithRetentionNote($"Special category personal data — needs a human decision ({Describe(findings)})", deadline),
                deadline);
        }

        if (findings.Any(IsFinancialOrIdentity))
        {
            return new FileClassification(
                Recommendation.Review,
                risk,
                WithRetentionNote($"Financial or identity data — needs a human decision ({Describe(findings)})", deadline),
                deadline);
        }

        if (findings.Count > 0)
        {
            if (StalenessPolicy.IsStale(file, nowUtc))
            {
                return new FileClassification(
                    Recommendation.Review,
                    risk,
                    WithRetentionNote($"{StalenessPolicy.Describe(file, nowUtc)}, and it holds personal data ({Describe(findings)})", deadline),
                    deadline);
            }

            if (deadline != RetentionDeadline.None)
            {
                return new FileClassification(
                    Recommendation.Review,
                    risk,
                    $"Holds personal data ({Describe(findings)}) — {LegalRetentionPolicy.Describe(deadline)}",
                    deadline);
            }

            return new FileClassification(
                Recommendation.Retain,
                risk,
                $"In use, and it holds personal data ({Describe(findings)})");
        }

        RuleVerdict verdict = ruleVerdict ?? InActiveUse;

        return new FileClassification(verdict.Recommendation, RiskLevel.None, verdict.Reason);
    }

    private static string WithRetentionNote(string reason, RetentionDeadline deadline) =>
        deadline == RetentionDeadline.None
            ? reason
            : $"{reason} — {LegalRetentionPolicy.Describe(deadline)}";

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
