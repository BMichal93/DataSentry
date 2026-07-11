using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class PeselDetectorTests
{
    private readonly PeselDetector _detector = new();

    [TestCase("90031500015", TestName = "Detect_BornInTheNineteenNineties_IsFound")]
    [TestCase("44050201233", TestName = "Detect_BornInTheForties_IsFound")]
    [TestCase("02210112348", TestName = "Detect_BornAfterTwoThousandSoTheMonthCarriesTheCentury_IsFound")]
    [TestCase("04210112342", TestName = "Detect_AnotherBornAfterTwoThousand_IsFound")]
    public void Detect_RealIdentityNumber_IsFound(string pesel)
    {
        PiiFinding? finding = _detector.Detect($"Employee {pesel} joined in March.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Identity));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.95));
        });
    }

    [TestCase("90031500016", TestName = "Detect_CheckDigitIsWrong_IsNotFound")]
    [TestCase("44050201234", TestName = "Detect_CheckDigitIsWrongOnAnOlderNumber_IsNotFound")]
    [TestCase("12345678901", TestName = "Detect_ElevenDigitReferenceNumber_IsNotFound")]
    [TestCase("90131500018", TestName = "Detect_ChecksumHoldsOverAThirteenthMonth_IsNotFound")]
    [TestCase("90023000017", TestName = "Detect_ChecksumHoldsOverTheThirtiethOfFebruary_IsNotFound")]
    [TestCase("99022900014", TestName = "Detect_ChecksumHoldsOverALeapDayInANonLeapYear_IsNotFound")]
    [TestCase("30610100010", TestName = "Detect_ChecksumHoldsOverABirthDateInTheTwentyThirdCentury_IsNotFound")]
    public void Detect_NotAnIdentityNumber_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Reference {candidate} in the ledger.");

        Assert.That(finding, Is.Null, "a valid checksum over a date that never happened is not a PESEL");
    }

    [Test]
    public void Detect_ChecksumHoldsOverABirthDateInTheEighteenHundreds_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Reference 85811200012 in the ledger.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.6), "a customer born in 1885 is likelier to be a reference number that got lucky");
        });
    }

    [Test]
    public void Detect_ElevenDigitsInsideALongerNumber_IsNotFound()
    {
        PiiFinding? finding = _detector.Detect("Order 9003150001512345 shipped.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_SeveralIdentityNumbers_ReportsTheCountAndNothingElse()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Name,PESEL
            Kowalski,90031500015
            Nowak,44050201233
            Wiśniewska,02210112348
            """);

        Assert.That(finding?.MatchCount, Is.EqualTo(3));
    }

    [Test]
    public void Detect_FileWithNoIdentityNumbers_FindsNothing()
    {
        PiiFinding? finding = _detector.Detect("The Q3 figures are attached. Nothing here identifies anybody.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_EmptyFile_FindsNothing()
    {
        PiiFinding? finding = _detector.Detect(string.Empty);

        Assert.That(finding, Is.Null);
    }
}
