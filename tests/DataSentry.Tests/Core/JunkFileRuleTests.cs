using DataSentry.Core.Classification;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class JunkFileRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly JunkFileRule _rule = new();

    [TestCase("C:/work/export.tmp")]
    [TestCase("C:/work/export.TMP")]
    [TestCase("C:/work/budget.bak")]
    [TestCase("C:/work/budget.old")]
    [TestCase("C:/work/chrome-setup.crdownload")]
    [TestCase("C:/work/Thumbs.db")]
    [TestCase("C:/work/desktop.ini")]
    [TestCase("C:/work/~$payroll.xlsx")]
    public void Evaluate_DisposableFile_RecommendsDelete(string filePath)
    {
        RuleVerdict? verdict = _rule.Evaluate(FileAt(filePath), Now);

        Assert.That(verdict?.Recommendation, Is.EqualTo(Recommendation.Delete));
    }

    [TestCase("C:/work/payroll.xlsx")]
    [TestCase("C:/work/contract.pdf")]
    [TestCase("C:/work/notes")]
    [TestCase("C:/work/archive.tmp.xlsx")]
    public void Evaluate_FileWorthKeeping_HasNothingToSay(string filePath)
    {
        RuleVerdict? verdict = _rule.Evaluate(FileAt(filePath), Now);

        Assert.That(verdict, Is.Null);
    }

    [Test]
    public void Evaluate_EmptyFile_RecommendsDelete()
    {
        var emptyFile = FileAt("C:/work/payroll.xlsx") with { SizeBytes = 0 };

        RuleVerdict? verdict = _rule.Evaluate(emptyFile, Now);

        Assert.Multiple(() =>
        {
            Assert.That(verdict?.Recommendation, Is.EqualTo(Recommendation.Delete));
            Assert.That(verdict?.Reason, Is.EqualTo("Empty file"));
        });
    }

    [Test]
    public void Evaluate_TemporaryFile_ExplainsItselfInPlainLanguage()
    {
        RuleVerdict? verdict = _rule.Evaluate(FileAt("C:/work/export.tmp"), Now);

        Assert.That(verdict?.Reason, Is.EqualTo("Temporary file (.tmp)"));
    }

    private static FileMetadata FileAt(string filePath) =>
        new(filePath, SizeBytes: 4_096, CreatedUtc: Now, LastModifiedUtc: Now, LastAccessedUtc: Now);
}
