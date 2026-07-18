using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class IbanDetectorTests
{
    private readonly IbanDetector _detector = new();

    [TestCase("PL61 1090 1014 0000 0712 1981 2874")]
    [TestCase("PL61109010140000071219812874")]
    [TestCase("DE89 3704 0044 0532 0130 00")]
    [TestCase("GB29 NWBK 6016 1331 9268 19")]
    [TestCase("FR14 2004 1010 0505 0001 3M02 606")]
    [TestCase("NL91ABNA0417164300")]
    public void Detect_RealAccountNumber_IsFound(string iban)
    {
        PiiFinding? finding = _detector.Detect($"Please remit to {iban} by Friday.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Financial));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.95));
        });
    }

    [TestCase("PL61 1090 1014 0000 0712 1981 2875", TestName = "Detect_ChecksumFailsOnALastDigitTypo_IsNotFound")]
    [TestCase("DE89 3704 0044 0532 0130 01", TestName = "Detect_ChecksumFailsOnAGermanAccount_IsNotFound")]
    [TestCase("GB29NWBK60161331926818", TestName = "Detect_ChecksumFailsOnAnUnspacedAccount_IsNotFound")]
    [TestCase("PL191234567890123456789012", TestName = "Detect_ChecksumPassesButLengthIsWrongForTheCountry_IsNotFound")]
    [TestCase("INV20240001234567890123", TestName = "Detect_InvoiceReferenceShapedLikeAnIban_IsNotFound")]
    [TestCase("PL61 1090", TestName = "Detect_StringTooShortToBeAnAccountNumber_IsNotFound")]
    public void Detect_NotAnAccountNumber_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Reference {candidate} in the ledger.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_ChecksumPassesUnderACountryCodeThatIssuesNoIbans_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Reference XX0912345678901234567890 in the ledger.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.6), "the checksum can be passed by luck; the country code is what settles it");
        });
    }

    [Test]
    public void Detect_LongerStringThatMerelyStartsLikeAnAccountNumber_IsNotFound()
    {
        PiiFinding? finding = _detector.Detect("Batch PL611090101400000712198128749Z was rejected.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_AccountNumberFollowedByAWordInCapitals_IsStillFound()
    {
        PiiFinding? finding = _detector.Detect("PL61 1090 1014 0000 0712 1981 2874 PAID IN FULL");

        Assert.That(finding?.MatchCount, Is.EqualTo(1), "the word after it is not part of the account number");
    }

    [Test]
    public void Detect_SeveralAccountNumbers_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Supplier,Account
            Kowalski,PL61 1090 1014 0000 0712 1981 2874
            Schmidt,DE89 3704 0044 0532 0130 00
            Dupont,FR14 2004 1010 0505 0001 3M02 606
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(3));
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(3));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("109010140000071219812874"),
                "a redacted snippet must never contain the account number it stands for");
        });
    }

    [Test]
    public void Detect_AccountNumber_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Please remit to PL61 1090 1014 0000 0712 1981 2874 by Friday.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "PL************************74" }));
    }

    [Test]
    public void Detect_FileWithNoAccountNumbers_FindsNothing()
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
