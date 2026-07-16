using DataSentry.Core.Detection;
using DataSentry.Core.Models;

namespace DataSentry.Tests.Core;

[TestFixture]
public class IpAddressDetectorTests
{
    private readonly IpAddressDetector _detector = new();

    [TestCase("192.168.0.14", TestName = "Detect_PrivateIpV4Address_IsFound")]
    [TestCase("83.11.240.255", TestName = "Detect_PublicIpV4Address_IsFound")]
    [TestCase("10.20.30.40", TestName = "Detect_IpV4AddressOfTwoDigitOctets_IsFound")]
    public void Detect_RealIpV4Address_IsFound(string address)
    {
        PiiFinding? finding = _detector.Detect($"Request came from {address} at 09:14.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Network));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.9));
        });
    }

    [TestCase("2001:0db8:85a3:0000:0000:8a2e:0370:7334", TestName = "Detect_IpV6AddressWrittenInFull_IsFound")]
    [TestCase("2001:db8:85a3::8a2e:370:7334", TestName = "Detect_IpV6AddressWrittenCompressed_IsFound")]
    [TestCase("fe80::1", TestName = "Detect_IpV6LinkLocalAddress_IsFound")]
    public void Detect_RealIpV6Address_IsFound(string address)
    {
        PiiFinding? finding = _detector.Detect($"Request came from {address} at 09:14.");

        Assert.Multiple(() =>
        {
            Assert.That(finding?.Category, Is.EqualTo(PiiCategory.Network));
            Assert.That(finding?.MatchCount, Is.EqualTo(1));
            Assert.That(finding?.Confidence, Is.EqualTo(0.95));
        });
    }

    [TestCase("Upgraded to v1.2.3.4 last night.", TestName = "Detect_VersionStringAnnouncedByALetterV_IsNotFound")]
    [TestCase("Running version 10.0.1.1 in production.", TestName = "Detect_VersionStringAnnouncedByTheWordVersion_IsNotFound")]
    [TestCase("Windows build 10.0.19041.1 shipped.", TestName = "Detect_FourPartVersionWithAPartAboveTwoFiftyFive_IsNotFound")]
    [TestCase("Octet 999.1.1.1 is nobody's address.", TestName = "Detect_FirstOctetOutOfRange_IsNotFound")]
    [TestCase("Padded 192.168.001.1 is not written the way an address is.", TestName = "Detect_OctetPaddedWithALeadingZero_IsNotFound")]
    [TestCase("The meeting runs 12:30:45 to 14:00.", TestName = "Detect_TimestampThatLooksLikeAnIpV6Address_IsNotFound")]
    [TestCase("The card is AB:CD:EF:12:34:56 on the switch.", TestName = "Detect_MacAddress_IsNotFound")]
    public void Detect_NotAnIpAddress_IsNotFound(string text)
    {
        PiiFinding? finding = _detector.Detect(text);

        Assert.That(finding, Is.Null, "a version string is not an IP address");
    }

    [Test]
    public void Detect_AddressWhoseEveryOctetIsASingleDigit_IsFoundButWithLowConfidence()
    {
        PiiFinding? finding = _detector.Detect("Resolver at 1.0.0.1 answered.");

        Assert.That(
            finding?.Confidence,
            Is.EqualTo(0.4),
            "a real address, and also exactly what a release number looks like — nothing in the text can tell them apart");
    }

    [Test]
    public void Detect_SeveralAddresses_ReportsTheCountAndOnlyRedactedSnippets()
    {
        PiiFinding? finding = _detector.Detect(
            """
            User,Last IP
            Kowalski,192.168.0.14
            Schmidt,83.11.240.255
            Dupont,2001:db8:85a3::8a2e:370:7334
            """);

        Assert.Multiple(() =>
        {
            Assert.That(finding?.MatchCount, Is.EqualTo(3));
            Assert.That(finding?.RedactedSnippets, Has.Count.EqualTo(3));
            Assert.That(
                finding?.RedactedSnippets,
                Has.None.Contains("168.0"),
                "a redacted snippet must never contain the address it stands for");
        });
    }

    [Test]
    public void Detect_IpV4Address_IsRedactedToItsFirstAndLastTwoCharacters()
    {
        PiiFinding? finding = _detector.Detect("Resolver at 192.168.0.14 answered.");

        Assert.That(finding?.RedactedSnippets, Is.EqualTo(new[] { "19********14" }));
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
