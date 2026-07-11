using DataSentry.Core.Classification;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class StaleFileRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly StaleFileRule _rule = new();

    [Test]
    public void Evaluate_FileNotTouchedInThreeYears_RecommendsDelete()
    {
        RuleVerdict? verdict = _rule.Evaluate(FileLastTouched(Now.AddYears(-3)), Now);

        Assert.Multiple(() =>
        {
            Assert.That(verdict?.Recommendation, Is.EqualTo(Recommendation.Delete));
            Assert.That(verdict?.Reason, Is.EqualTo("Not opened in 3 years"));
        });
    }

    [Test]
    public void Evaluate_FileModifiedLastMonth_HasNothingToSay()
    {
        RuleVerdict? verdict = _rule.Evaluate(FileLastTouched(Now.AddMonths(-1)), Now);

        Assert.That(verdict, Is.Null);
    }

    [Test]
    public void Evaluate_FileEditedYearsAgoButOpenedYesterday_HasNothingToSay()
    {
        var file = new FileMetadata(
            "C:/work/tax-return-2019.pdf",
            SizeBytes: 4_096,
            CreatedUtc: Now.AddYears(-7),
            LastModifiedUtc: Now.AddYears(-7),
            LastAccessedUtc: Now.AddDays(-1));

        RuleVerdict? verdict = _rule.Evaluate(file, Now);

        Assert.That(verdict, Is.Null, "a file somebody read yesterday is not forgotten, whenever it was last written");
    }

    [Test]
    public void Evaluate_FileOneDayShortOfTheThreshold_HasNothingToSay()
    {
        RuleVerdict? verdict = _rule.Evaluate(
            FileLastTouched(Now.AddDays(-StalenessPolicy.StaleAfterDays + 1)),
            Now);

        Assert.That(verdict, Is.Null);
    }

    [Test]
    public void Evaluate_FileExactlyOnTheThreshold_RecommendsDelete()
    {
        RuleVerdict? verdict = _rule.Evaluate(
            FileLastTouched(Now.AddDays(-StalenessPolicy.StaleAfterDays)),
            Now);

        Assert.That(verdict?.Recommendation, Is.EqualTo(Recommendation.Delete));
    }

    private static FileMetadata FileLastTouched(DateTimeOffset lastTouchedUtc) =>
        new(
            "C:/work/quarterly-export.xlsx",
            SizeBytes: 4_096,
            CreatedUtc: lastTouchedUtc,
            LastModifiedUtc: lastTouchedUtc,
            LastAccessedUtc: lastTouchedUtc);
}
