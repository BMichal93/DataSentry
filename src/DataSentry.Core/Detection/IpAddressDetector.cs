using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// IP addresses, v4 and v6. An address is personal data under the GDPR — it is how a person is found
/// again — but four numbers separated by dots is also how software says which version of itself it is,
/// and a log file is full of both.
/// </summary>
/// <remarks>
/// Nothing validates an IPv4 address the way a checksum validates an account number: <c>1.2.3.4</c> is
/// a perfectly good address and a perfectly good version number, and no amount of parsing will tell
/// them apart. So the tell is not the address, it is what is written next to it — a <c>v</c>, the word
/// "version", a fourth part that runs past 255 — and the shape of the number itself, because real
/// addresses use the whole of each octet and version numbers count from one. IPv6 has no such problem:
/// nothing else looks like it.
/// </remarks>
public sealed partial class IpAddressDetector : IPiiDetector
{
    /// <summary>Nothing else in a text file looks like an IPv6 address.</summary>
    private const double IpV6Confidence = 0.95;

    /// <summary>Four octets, at least one of which is bigger than any version number gets.</summary>
    private const double IpV4Confidence = 0.9;

    /// <summary>
    /// Four octets, every one of them a single digit — <c>1.0.0.1</c>. A real address, and also exactly
    /// what a release number looks like. Reported, because a machine really does live at 1.0.0.1, but
    /// never at full confidence.
    /// </summary>
    private const double VersionShapedConfidence = 0.4;

    /// <summary>An octet above this is not a version number anybody has shipped, and settles the question.</summary>
    private const int LargestSingleDigitOctet = 9;

    /// <summary>What software says just before it tells you which version of itself it is.</summary>
    private static readonly string[] VersionWords = ["v", "version", "wersja", "rev", "build"];

    public string Name => "IP address";

    public PiiCategory Category => PiiCategory.Network;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;
        var snippets = new List<string>();

        foreach (Match candidate in IpShapedText().Matches(text))
        {
            double candidateConfidence = candidate.Groups["v6"].Success
                ? ScoreIpV6(candidate.Value)
                : ScoreIpV4(candidate.Value, text, candidate.Index);

            if (candidateConfidence == 0)
            {
                continue;
            }

            matchCount++;
            confidence = Math.Max(confidence, candidateConfidence);
            snippets.Add(SnippetRedactor.Redact(candidate.Value));
        }

        return matchCount == 0
            ? null
            : new PiiFinding(Category, Name, matchCount, confidence, snippets);
    }

    private static double ScoreIpV6(string candidate) =>
        IPAddress.TryParse(candidate, out IPAddress? address)
        && address.AddressFamily == AddressFamily.InterNetworkV6
            ? IpV6Confidence
            : 0;

    private static double ScoreIpV4(string candidate, string text, int startIndex)
    {
        string[] octets = candidate.Split('.');

        if (octets.Any(octet => !IsOctet(octet)) || IsVersionNumber(text, startIndex))
        {
            return 0;
        }

        return octets.Any(octet => int.Parse(octet) > LargestSingleDigitOctet)
            ? IpV4Confidence
            : VersionShapedConfidence;
    }

    /// <summary>0 to 255, and written the way an address is written: <c>010</c> is padding, not an octet.</summary>
    private static bool IsOctet(string octet) =>
        octet.Length <= 3
        && (octet.Length == 1 || octet[0] != '0')
        && int.Parse(octet) <= 255;

    /// <summary>
    /// Whether the thing in front of these four numbers has already said what they are. "v1.2.3.4" and
    /// "Version 1.2.3.4" are software announcing itself, and no machine is being identified.
    /// </summary>
    private static bool IsVersionNumber(string text, int startIndex)
    {
        ReadOnlySpan<char> before = text.AsSpan(0, startIndex).TrimEnd(" :=".AsSpan());

        foreach (string versionWord in VersionWords)
        {
            if (EndsWithWord(before, versionWord))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithWord(ReadOnlySpan<char> text, string word) =>
        text.EndsWith(word, StringComparison.OrdinalIgnoreCase)
        && (text.Length == word.Length || !char.IsLetterOrDigit(text[^(word.Length + 1)]));

    /// <summary>
    /// Either family, in one pass. The v4 half takes any four dotted numbers and lets the octets be
    /// checked afterwards, so that <c>10.0.19041.1</c> is looked at and rejected rather than never
    /// looked at: a regex that only matches valid octets would quietly find <c>10.0.1</c> inside it and
    /// call that an address instead.
    /// </summary>
    [GeneratedRegex(
        """
        (?<![0-9.])[0-9]{1,4}(?:\.[0-9]{1,4}){3}(?![0-9.])
        | (?<v6>(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![0-9A-Fa-f:]))
        """,
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex IpShapedText();
}
