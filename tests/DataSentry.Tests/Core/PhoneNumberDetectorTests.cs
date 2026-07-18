using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class PhoneNumberDetectorTests
{
    private readonly PhoneNumberDetector _detector = new();

    [TestCase("+48 601 234 567", TestName = "Detect_PolishMobileWrittenInternationally_IsFound")]
    [TestCase("+48601234567", TestName = "Detect_PolishMobileWrittenUnspaced_IsFound")]
    [TestCase("+49 30 901820", TestName = "Detect_GermanLandline_IsFound")]
    [TestCase("+33-1-42-68-53-00", TestName = "Detect_FrenchNumberWrittenWithHyphens_IsFound")]
    [TestCase("+420 234 567 890", TestName = "Detect_CzechNumberUnderAThreeDigitCallingCode_IsFound")]
    public void Detect_RealPhoneNumber_IsFound(string phoneNumber)
    {
        PiiFinding? finding = _detector.Detect($"Call {phoneNumber} before noon.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Contact));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.9));
        });
    }

    [TestCase("+48 601", TestName = "Detect_TooFewDigitsToBeAnybodysLine_IsNotFound")]
    [TestCase("+48 601 234 567 890 123", TestName = "Detect_TooManyDigitsToBeAnybodysLine_IsNotFound")]
    [TestCase("1234", TestName = "Detect_ShortNumber_IsNotFound")]
    [TestCase("12345678", TestName = "Detect_EightDigitReferenceNumber_IsNotFound")]
    [TestCase("1234567890", TestName = "Detect_TenDigitRunOfDigits_IsNotFound")]
    [TestCase("2024-11-05", TestName = "Detect_Date_IsNotFound")]
    public void Detect_NotAPhoneNumber_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Reference {candidate} in the ledger.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_NineDigitsWithNoCallingCode_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Call 601 234 567 before noon.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.55), "nine digits is also an order number; the calling code is what settles it");
        });
    }

    [Test]
    public void Detect_NumberWrittenInternationally_IsNotCountedASecondTimeAsANationalOne()
    {
        PiiFinding? finding = _detector.Detect("Call +48 601 234 567 before noon.");

        Assert.That(finding?.MatchCount, Is.EqualTo(1), "the nine digits inside it are the same number, not another one");
    }

    [Test]
    public void Detect_CallingCodeFromOutsideEurope_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Call +1 555 123 4567 before noon.");

        Assert.That(finding?.Confidence, Is.EqualTo(0.6));
    }

    [Test]
    public void Detect_SeveralPhoneNumbers_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Name,Phone
            Kowalski,+48 601 234 567
            Schmidt,+49 30 901820
            Dupont,+33 1 42 68 53 00
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(3));
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(3));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("601234567"),
                "a redacted snippet must never contain the phone number it stands for");
        });
    }

    [Test]
    public void Detect_PhoneNumber_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Call +48 601 234 567 before noon.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "+4***********67" }));
    }

    [Test]
    public void Detect_FileWithNoPhoneNumbers_FindsNothing()
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
