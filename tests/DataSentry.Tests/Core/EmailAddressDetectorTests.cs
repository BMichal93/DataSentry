using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class EmailAddressDetectorTests
{
    private readonly EmailAddressDetector _detector = new();

    [TestCase("anna.kowalska@firma.pl", TestName = "Detect_PolishBusinessAddress_IsFound")]
    [TestCase("j.smith+invoices@acme.co.uk", TestName = "Detect_AddressWithATagAndASecondLevelDomain_IsFound")]
    [TestCase("payroll@contoso.com", TestName = "Detect_OrdinaryCorporateAddress_IsFound")]
    [TestCase("m_nowak99@wp.pl", TestName = "Detect_AddressWithDigitsAndAnUnderscore_IsFound")]
    public void Detect_RealEmailAddress_IsFound(string emailAddress)
    {
        PiiFinding? finding = _detector.Detect($"Send the report to {emailAddress} please.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Contact));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.9));
        });
    }

    [TestCase("nobody@", TestName = "Detect_LocalPartWithNoDomain_IsNotFound")]
    [TestCase("@nowhere.com", TestName = "Detect_DomainWithNoLocalPart_IsNotFound")]
    [TestCase("firma.pl", TestName = "Detect_DomainOnItsOwn_IsNotFound")]
    [TestCase("anna@example.com", TestName = "Detect_AddressUnderTheDomainReservedForDocumentation_IsNotFound")]
    [TestCase("anna@mail.example.com", TestName = "Detect_AddressUnderTheReservedDomainsSubdomain_IsNotFound")]
    [TestCase("build@runner.test", TestName = "Detect_AddressUnderTheTopLevelDomainReservedForTesting_IsNotFound")]
    public void Detect_NotAnEmailAddress_IsNotFound(string candidate)
    {
        PiiFinding? finding = _detector.Detect($"Contact {candidate} for details.");

        Assert.That(finding, Is.Null);
    }

    [Test]
    public void Detect_AddressUnderATopLevelDomainNobodyRegisters_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("The asset is named logo@2x.png in the bundle.");

        Assert.That(
            finding?.Confidence,
            Is.EqualTo(0.5),
            "with no checksum to fall back on, the top-level domain is the only thing that separates an address from a file name");
    }

    [Test]
    public void Detect_SeveralAddresses_ReportsTheCountAndNothingElse()
    {
        PiiFinding? finding = _detector.Detect(
            """
            Name,Email
            Kowalska,anna.kowalska@firma.pl
            Schmidt,schmidt@contoso.de
            Dupont,dupont@acme.fr
            """);

        Assert.That(finding?.MatchCount, Is.EqualTo(3));
    }

    [Test]
    public void Detect_FileWithNoAddresses_FindsNothing()
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
