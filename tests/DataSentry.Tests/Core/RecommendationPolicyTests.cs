using System.Collections.Generic;
using DataSentry.Core.Classification;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class RecommendationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly RuleVerdict Junk = new(Recommendation.Delete, "Temporary file (.tmp)");

    /// <summary>
    /// Every category, at every count that can change the answer, in a file the ordinary rules would
    /// happily delete. The recommendation is the whole point of the table: nothing with personal data
    /// in it is ever deleted on this tool's say-so.
    /// </summary>
    [TestCase(PiiCategory.SpecialCategory, 1, Recommendation.Review, RiskLevel.Critical)]
    [TestCase(PiiCategory.SpecialCategory, 12, Recommendation.Review, RiskLevel.Critical)]
    [TestCase(PiiCategory.Financial, 1, Recommendation.Review, RiskLevel.High)]
    [TestCase(PiiCategory.Financial, 12, Recommendation.Review, RiskLevel.High)]
    [TestCase(PiiCategory.Identity, 1, Recommendation.Review, RiskLevel.High)]
    [TestCase(PiiCategory.Identity, 12, Recommendation.Review, RiskLevel.High)]
    [TestCase(PiiCategory.Contact, 1, Recommendation.Review, RiskLevel.Medium)]
    [TestCase(PiiCategory.Contact, 12, Recommendation.Review, RiskLevel.Medium)]
    [TestCase(PiiCategory.Network, 1, Recommendation.Review, RiskLevel.Low)]
    [TestCase(PiiCategory.Keyword, 1, Recommendation.Review, RiskLevel.Low)]
    public void Decide_StaleJunkFileHoldingPersonalData_IsNeverDeleted(
        PiiCategory category,
        int matchCount,
        Recommendation expectedRecommendation,
        RiskLevel expectedRisk)
    {
        FileClassification classification = RecommendationPolicy.Decide(
            StaleFile,
            Junk,
            [Finding(category, matchCount)],
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(expectedRecommendation));
            Assert.That(classification.RiskLevel, Is.EqualTo(expectedRisk));
        });
    }

    /// <summary>
    /// The same table over a file still in use. Here the categories part company: what identifies a
    /// bank account or a person is worth a human's time whatever the file is; a colleague's email
    /// address in a document somebody edited last week is not.
    /// </summary>
    [TestCase(PiiCategory.SpecialCategory, Recommendation.Review)]
    [TestCase(PiiCategory.Financial, Recommendation.Review)]
    [TestCase(PiiCategory.Identity, Recommendation.Review)]
    [TestCase(PiiCategory.Contact, Recommendation.Retain)]
    [TestCase(PiiCategory.Network, Recommendation.Retain)]
    [TestCase(PiiCategory.Keyword, Recommendation.Retain)]
    public void Decide_FileInUseHoldingPersonalData_ReviewsOnlyWhatIsWorthAHumansTime(
        PiiCategory category,
        Recommendation expectedRecommendation)
    {
        FileClassification classification = RecommendationPolicy.Decide(
            FileInUse,
            ruleVerdict: null,
            [Finding(category, 3)],
            Now);

        Assert.That(classification.Recommendation, Is.EqualTo(expectedRecommendation));
    }

    [Test]
    public void Decide_SpecialCategoryAlongsideOrdinaryPii_IsRatedByTheWorstOfIt()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            FileInUse,
            ruleVerdict: null,
            [Finding(PiiCategory.Contact, 12), Finding(PiiCategory.SpecialCategory, 1)],
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(Recommendation.Review));
            Assert.That(classification.RiskLevel, Is.EqualTo(RiskLevel.Critical));
        });
    }

    [Test]
    public void Decide_NoPersonalData_LeavesTheVerdictToTheOrdinaryRules()
    {
        FileClassification classification = RecommendationPolicy.Decide(StaleFile, Junk, [], Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(Recommendation.Delete));
            Assert.That(classification.Reason, Is.EqualTo("Temporary file (.tmp)"));
            Assert.That(classification.RiskLevel, Is.EqualTo(RiskLevel.None));
        });
    }

    [Test]
    public void Decide_NoPersonalDataAndNoRuleAgainstIt_RetainsTheFile()
    {
        FileClassification classification = RecommendationPolicy.Decide(FileInUse, ruleVerdict: null, [], Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(Recommendation.Retain));
            Assert.That(classification.Reason, Is.EqualTo("In active use"));
        });
    }

    [Test]
    public void Decide_FileHoldingAccountNumbers_SaysHowManyAndNeverWhichOnes()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            StaleFile,
            Junk,
            [Finding(PiiCategory.Financial, 3)],
            Now);

        Assert.That(
            classification.Reason,
            Is.EqualTo("Financial or identity data — needs a human decision (3 IBANs)"));
    }

    [Test]
    public void Decide_FileHoldingOneAccountNumber_CountsItInEnglish()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            StaleFile,
            Junk,
            [Finding(PiiCategory.Financial, 1)],
            Now);

        Assert.That(classification.Reason, Does.Contain("(1 IBAN)"));
    }

    [Test]
    public void Decide_StaleFileHoldingOrdinaryPii_SaysBothWhyItIsOldAndWhatIsInIt()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            StaleFile,
            ruleVerdict: null,
            [Finding(PiiCategory.Contact, 12, detectorName: "email address")],
            Now);

        Assert.That(
            classification.Reason,
            Is.EqualTo("Not opened in 3 years, and it holds personal data (12 email addresses)"));
    }

    /// <summary>
    /// Ordinary personal data in a file still in use is normally left alone — but not when the file has
    /// been kept to the edge of its legal welcome. A spreadsheet people still open but nobody has
    /// changed in six years is exactly the document whose legal basis for existing needs a decision.
    /// </summary>
    [TestCase(-6, Recommendation.Review, RetentionDeadline.Breached)]
    [TestCase(-2, Recommendation.Retain, RetentionDeadline.None)]
    public void Decide_OrdinaryPiiInFileReadButNotChangedForYears_ReviewsOnceRetentionRunsOut(
        int yearsSinceLastEdit,
        Recommendation expectedRecommendation,
        RetentionDeadline expectedDeadline)
    {
        var stillBeingRead = new FileMetadata(
            "C:/work/payroll-export.xlsx",
            SizeBytes: 8_192,
            CreatedUtc: Now.AddYears(yearsSinceLastEdit),
            LastModifiedUtc: Now.AddYears(yearsSinceLastEdit),
            LastAccessedUtc: Now.AddDays(-2));

        FileClassification classification = RecommendationPolicy.Decide(
            stillBeingRead,
            ruleVerdict: null,
            [Finding(PiiCategory.Contact, 12, detectorName: "email address")],
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(expectedRecommendation));
            Assert.That(classification.RetentionDeadline, Is.EqualTo(expectedDeadline));
        });
    }

    [Test]
    public void Decide_OrdinaryPiiPastRetentionInFileStillRead_SaysWhyItIsOnTheList()
    {
        var stillBeingRead = new FileMetadata(
            "C:/work/payroll-export.xlsx",
            SizeBytes: 8_192,
            CreatedUtc: Now.AddYears(-6),
            LastModifiedUtc: Now.AddYears(-6),
            LastAccessedUtc: Now.AddDays(-2));

        FileClassification classification = RecommendationPolicy.Decide(
            stillBeingRead,
            ruleVerdict: null,
            [Finding(PiiCategory.Contact, 12, detectorName: "email address")],
            Now);

        Assert.That(
            classification.Reason,
            Is.EqualTo("Holds personal data (12 email addresses) — kept longer than the 5 years documents like this usually must be"));
    }

    [Test]
    public void Decide_FinancialDataPastRetention_KeepsReviewAndAddsTheRetentionNote()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            FileLastTouched(Now.AddYears(-6)),
            ruleVerdict: null,
            [Finding(PiiCategory.Financial, 3)],
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(Recommendation.Review));
            Assert.That(classification.RetentionDeadline, Is.EqualTo(RetentionDeadline.Breached));
            Assert.That(
                classification.Reason,
                Is.EqualTo("Financial or identity data — needs a human decision (3 IBANs) — kept longer than the 5 years documents like this usually must be"));
        });
    }

    [Test]
    public void Decide_OrdinaryPiiApproachingRetention_ReviewsBeforeTheClockRunsOut()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            FileLastTouched(Now.AddYears(-5).AddDays(30)),
            ruleVerdict: null,
            [Finding(PiiCategory.Contact, 2, detectorName: "email address")],
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(classification.Recommendation, Is.EqualTo(Recommendation.Review));
            Assert.That(classification.RetentionDeadline, Is.EqualTo(RetentionDeadline.Approaching));
        });
    }

    /// <summary>A clean file has no retention clock: only personal data has to justify being kept.</summary>
    [Test]
    public void Decide_NoPersonalDataInAncientFile_HasNoRetentionDeadline()
    {
        FileClassification classification = RecommendationPolicy.Decide(
            FileLastTouched(Now.AddYears(-8)),
            Junk,
            [],
            Now);

        Assert.That(classification.RetentionDeadline, Is.EqualTo(RetentionDeadline.None));
    }

    private static FileMetadata StaleFile => FileLastTouched(Now.AddYears(-3));

    private static FileMetadata FileInUse => FileLastTouched(Now.AddDays(-2));

    private static FileMetadata FileLastTouched(DateTimeOffset lastTouchedUtc) =>
        new(
            "C:/work/payroll-export.xlsx",
            SizeBytes: 8_192,
            CreatedUtc: lastTouchedUtc,
            LastModifiedUtc: lastTouchedUtc,
            LastAccessedUtc: lastTouchedUtc);

    private static PiiFinding Finding(PiiCategory category, int matchCount, string detectorName = "IBAN") =>
        new(category, detectorName, matchCount, Confidence: 0.95);
}
