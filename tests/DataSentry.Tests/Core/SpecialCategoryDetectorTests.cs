using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class SpecialCategoryDetectorTests
{
    private readonly SpecialCategoryDetector _detector = new();

    [TestCase("The patient was diagnosed with a chronic illness in March.", TestName = "Detect_HealthDataInEnglish_IsFound")]
    [TestCase("Dokumentacja medyczna: diagnoza postawiona w marcu.", TestName = "Detect_HealthDataInPolish_IsFound")]
    [TestCase("Biometric data: fingerprint and iris scan on file.", TestName = "Detect_BiometricData_IsFound")]
    [TestCase("Trade union membership and union dues, per member.", TestName = "Detect_TradeUnionMembership_IsFound")]
    [TestCase("Records political party affiliation and religious belief.", TestName = "Detect_PoliticalOpinionAndReligiousBelief_IsFound")]
    [TestCase("Sexual orientation was recorded alongside ethnic origin.", TestName = "Detect_SexualOrientationAndEthnicOrigin_IsFound")]
    public void Detect_SpecialCategoryData_IsFoundAtHighConfidence(string text)
    {
        PiiFinding? finding = _detector.Detect(text);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.SpecialCategory), "an Art. 9 hit must never be filed as an ordinary keyword");
            Assert.That(finding?.Confidence, Is.EqualTo(0.9), "several different terms in one file is a record about a person, not a turn of phrase");
        });
    }

    [TestCase("The Q3 figures are attached. Nothing here identifies anybody.", TestName = "Detect_OrdinaryCorrespondence_IsNotFound")]
    [TestCase("Supplier invoices for the quarter, filed by account.", TestName = "Detect_FinanceDocumentWithNoArticleNineTerms_IsNotFound")]
    [TestCase("The racetrack booking is confirmed for Saturday.", TestName = "Detect_WordThatOnlyContainsATermInsideALongerWord_IsNotFound")]
    [TestCase("Deployment runbook for the union of the two datasets.", TestName = "Detect_TheWordUnionWithoutTheTermItBelongsTo_IsNotFound")]
    [TestCase("", TestName = "Detect_EmptyFile_FindsNothing")]
    [TestCase("Health and safety notice: fire drill on Tuesday.", TestName = "Detect_HealthAndSafetyNoticeWhichIsAboutNobody_IsNotFound")]
    public void Detect_NoSpecialCategoryData_IsNotFound(string text)
    {
        PiiFinding? finding = _detector.Detect(text);

        Assert.That(finding, Is.Null, "a tool that cries wolf is a tool the user learns to ignore");
    }

    [Test]
    public void Detect_ASingleTermOnItsOwn_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Attached is the prescription you asked about.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.6), "one term could be a figure of speech; it still gets looked at, but not at full confidence");
        });
    }

    [Test]
    public void Detect_TermsWrittenInAnyCase_AreStillFound()
    {
        PiiFinding? finding = _detector.Detect("BLOOD GROUP and Vaccination status, per employee.");

        Assert.That(finding?.MatchCount, Is.EqualTo(2));
    }

    [Test]
    public void Detect_TheSameTermSeveralTimes_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Employee,Diagnosis
            Kowalski,diagnosis withheld
            Nowak,diagnosis withheld
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(3), "the count is how often, never what");
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(3));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("diagnosis").IgnoreCase,
                "a redacted snippet must never contain the term it stands for");
        });
    }

    [Test]
    public void Detect_Term_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Diagnosis withheld pending review.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "Di*****is" }));
    }
}
