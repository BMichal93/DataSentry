using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class PolishIdCardDetectorTests
{
    private readonly PolishIdCardDetector _detector = new();

    [TestCase("ABA598120", TestName = "Detect_DocumentNumberWrittenSolid_IsFound")]
    [TestCase("ABA 598120", TestName = "Detect_DocumentNumberWithTheCustomarySpace_IsFound")]
    [TestCase("XYZ712345", TestName = "Detect_AnotherValidDocumentNumber_IsFound")]
    public void Detect_RealDocumentNumber_IsFound(string documentNumber)
    {
        PiiFinding? finding = _detector.Detect($"Seria i nr dokumentu: {documentNumber}, wydany 2019.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Identity));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.9));
        });
    }

    [TestCase("ABA498120", TestName = "Detect_CheckDigitIsWrong_IsNotFound")]
    [TestCase("ABC123456", TestName = "Detect_BookingCodeShapedString_IsNotFound")]
    [TestCase("aba598120", TestName = "Detect_LowercaseSoNotADocumentSeries_IsNotFound")]
    public void Detect_NotADocumentNumber_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Reference {candidate} in the ledger.");

        Assert.That(finding, Is.Null, "three letters and six digits is only an ID card number when the checksum agrees");
    }

    [Test]
    public void Detect_DocumentNumberInsideALongerCode_IsNotFound()
    {
        PiiFinding? finding = _detector.Detect("Shipment SKUABA5981204 dispatched.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_SeveralDocumentNumbers_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Name,Document
            Kowalski,ABA598120
            Nowak,XYZ712345
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(2));
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(2));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("598120"),
                "a redacted snippet must never contain the document number it stands for");
        });
    }

    [Test]
    public void Detect_DocumentNumber_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Reference ABA598120 in the ledger.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "AB*****20" }));
    }

    [Test]
    public void Detect_FileWithNoDocumentNumbers_FindsNothing()
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
