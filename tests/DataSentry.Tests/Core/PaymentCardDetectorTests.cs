using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class PaymentCardDetectorTests
{
    private readonly PaymentCardDetector _detector = new();

    [TestCase("4111 1111 1111 1111", TestName = "Detect_SixteenDigitCardWrittenInGroups_IsFound")]
    [TestCase("4012888888881881", TestName = "Detect_SixteenDigitCardWrittenUnspaced_IsFound")]
    [TestCase("5555-5555-5555-4444", TestName = "Detect_CardWrittenWithHyphens_IsFound")]
    [TestCase("3782 822463 10005", TestName = "Detect_FifteenDigitCard_IsFound")]
    [TestCase("6011111111111117", TestName = "Detect_CardUnderAnotherIssuer_IsFound")]
    public void Detect_RealCardNumber_IsFound(string card)
    {
        PiiFinding? finding = _detector.Detect($"Charged to {card} on the 3rd.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Financial));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.95));
        });
    }

    [TestCase("4111 1111 1111 1112", TestName = "Detect_LuhnFailsOnALastDigitTypo_IsNotFound")]
    [TestCase("5555555555554445", TestName = "Detect_LuhnFailsOnAnUnspacedNumber_IsNotFound")]
    [TestCase("0000000000000000", TestName = "Detect_ColumnOfZeroesThatLuhnHappilyAccepts_IsNotFound")]
    [TestCase("8888888888888888", TestName = "Detect_SixteenOfTheSameDigit_IsNotFound")]
    [TestCase("4567890123456789012", TestName = "Detect_CountingUpUnderARealIssuersPrefix_IsNotFound")]
    [TestCase("3210987654321098", TestName = "Detect_CountingDown_IsNotFound")]
    public void Detect_NotACardNumber_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Reference {candidate} in the ledger.");

        Assert.That(finding, Is.Null, "Luhn is a typo check, not an identity check — it accepts all of these");
    }

    [Test]
    public void Detect_LuhnHoldsButNoIssuerHandsOutNumbersOfThatShape_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Reference 7992739871300008 in the ledger.");

        Assert.That(finding?.Confidence, Is.EqualTo(0.6), "Luhn alone can be passed by luck; the issuer is what settles it");
    }

    [Test]
    public void Detect_AccountNumberWrittenInGroupsOfFour_IsNotMistakenForACard()
    {
        PiiFinding? finding = _detector.Detect("Please remit to PL61 1090 1014 0000 0712 1981 2874 by Friday.");

        Assert.That(finding, Is.Null, "sixteen of the twenty-six digits in an account number are not a card number");
    }

    [Test]
    public void Detect_SixteenDigitsInsideALongerRunOfDigits_IsNotFound()
    {
        PiiFinding? finding = _detector.Detect("Batch 411111111111111100042 was rejected.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_SeveralCardNumbers_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Customer,Card
            Kowalski,4111 1111 1111 1111
            Schmidt,5555 5555 5555 4444
            Dupont,3782 822463 10005
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(3));
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(3));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("55554444"),
                "a redacted snippet must never contain the card number it stands for");
        });
    }

    [Test]
    public void Detect_CardNumber_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Card on file: 4111 1111 1111 1111.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "41***************11" }));
    }

    [Test]
    public void Detect_FileWithNoCardNumbers_FindsNothing()
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
