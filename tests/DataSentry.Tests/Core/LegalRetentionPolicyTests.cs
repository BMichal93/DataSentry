using DataSentry.Core.Classification;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class LegalRetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void DeadlineFor_FileWellInsideRetentionPeriod_ReportsNone()
    {
        FileMetadata file = FileFinished(Now.AddYears(-2));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.None));
    }

    [Test]
    public void DeadlineFor_FileJustOutsideWarningWindow_ReportsNone()
    {
        FileMetadata file = FileFinished(
            Now.AddYears(-LegalRetentionPolicy.TypicalRetentionYears)
               .AddDays(LegalRetentionPolicy.ApproachingWindowDays + 1));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.None));
    }

    [Test]
    public void DeadlineFor_FileInsideWarningWindow_ReportsApproaching()
    {
        FileMetadata file = FileFinished(
            Now.AddYears(-LegalRetentionPolicy.TypicalRetentionYears).AddDays(30));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.Approaching));
    }

    [Test]
    public void DeadlineFor_FileAtTheEdgeOfWarningWindow_ReportsApproaching()
    {
        FileMetadata file = FileFinished(
            Now.AddYears(-LegalRetentionPolicy.TypicalRetentionYears)
               .AddDays(LegalRetentionPolicy.ApproachingWindowDays));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.Approaching));
    }

    [Test]
    public void DeadlineFor_FileExactlyAtDeadline_ReportsBreached()
    {
        FileMetadata file = FileFinished(Now.AddYears(-LegalRetentionPolicy.TypicalRetentionYears));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.Breached));
    }

    [Test]
    public void DeadlineFor_FileOlderThanRetentionPeriod_ReportsBreached()
    {
        FileMetadata file = FileFinished(Now.AddYears(-7));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.Breached));
    }

    /// <summary>
    /// The clock runs from the later of created and last modified: a document still being edited has
    /// not been forgotten, however old the original file is.
    /// </summary>
    [Test]
    public void DeadlineFor_OldFileEditedLastWeek_ReportsNone()
    {
        var file = new FileMetadata(
            "C:/finance/invoices-2019.xlsx",
            SizeBytes: 8_192,
            CreatedUtc: Now.AddYears(-7),
            LastModifiedUtc: Now.AddDays(-7),
            LastAccessedUtc: Now.AddDays(-7));

        Assert.That(LegalRetentionPolicy.DeadlineFor(file, Now), Is.EqualTo(RetentionDeadline.None));
    }

    [Test]
    public void Describe_NoDeadline_HasNothingToSay()
    {
        Assert.That(LegalRetentionPolicy.Describe(RetentionDeadline.None), Is.Empty);
    }

    [Test]
    public void Describe_BreachedDeadline_SpeaksHuman()
    {
        Assert.That(
            LegalRetentionPolicy.Describe(RetentionDeadline.Breached),
            Is.EqualTo("kept longer than the 5 years documents like this usually must be"));
    }

    private static FileMetadata FileFinished(DateTimeOffset lastTouchedUtc) =>
        new(
            "C:/finance/invoices-2019.xlsx",
            SizeBytes: 8_192,
            CreatedUtc: lastTouchedUtc,
            LastModifiedUtc: lastTouchedUtc,
            LastAccessedUtc: lastTouchedUtc);
}
