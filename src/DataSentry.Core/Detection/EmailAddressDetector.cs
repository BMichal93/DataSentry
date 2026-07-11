using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// Email addresses. There is no checksum to fall back on here, so the top-level domain does the work
/// the country code does for an IBAN: an address ending in a domain the world actually uses is an
/// address, and one ending in anything else is a sprite sheet, a file name or a Twitter handle that
/// happens to have an at-sign in it.
/// </summary>
public sealed partial class EmailAddressDetector : IPiiDetector
{
    /// <summary>A real top-level domain. About as sure as an address without a check digit gets.</summary>
    private const double RealTopLevelDomainConfidence = 0.9;

    /// <summary>
    /// Shaped like an address, but ending in something nobody registers domains under. Reported,
    /// because the list below is not the whole internet, but reported as the guess it is.
    /// </summary>
    private const double UnrecognisedTopLevelDomainConfidence = 0.5;

    /// <summary>
    /// The top-level domains a European business writes down. Not the whole registry — the point is
    /// not to bless every address on earth, it is to separate <c>anna@firma.pl</c> from
    /// <c>logo@2x.png</c>, and a list that ends at the domains people actually correspond under does
    /// that better than a list that includes every extension a file could have.
    /// </summary>
    private static readonly HashSet<string> RealTopLevelDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "edu", "gov", "mil", "int", "info", "biz", "io", "co", "eu",
        "pl", "de", "fr", "uk", "nl", "es", "it", "cz", "sk", "se", "no", "dk", "fi", "ie",
        "at", "be", "ch", "pt", "hu", "ro", "bg", "gr", "lt", "lv", "ee", "si", "hr", "lu",
        "mt", "cy", "us", "ca", "au"
    };

    /// <summary>
    /// The domains RFC 2606 sets aside so that documentation has something to point at. An address
    /// under one of them identifies nobody, and reporting it as personal data would teach the user to
    /// ignore the tool.
    /// </summary>
    private static readonly HashSet<string> ReservedForDocumentation = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.com", "example.net", "example.org", "example", "test", "invalid", "localhost"
    };

    public string Name => "email address";

    public PiiCategory Category => PiiCategory.Contact;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;

        foreach (Match candidate in EmailShapedText().Matches(text))
        {
            double candidateConfidence = Score(candidate.Groups["domain"].Value);

            if (candidateConfidence == 0)
            {
                continue;
            }

            matchCount++;
            confidence = Math.Max(confidence, candidateConfidence);
        }

        return matchCount == 0
            ? null
            : new PiiFinding(Category, Name, matchCount, confidence);
    }

    /// <summary>How sure we are that somebody could be written to here — zero when nobody could.</summary>
    private static double Score(string domain)
    {
        if (IsReservedForDocumentation(domain))
        {
            return 0;
        }

        string topLevelDomain = domain[(domain.LastIndexOf('.') + 1)..];

        return RealTopLevelDomains.Contains(topLevelDomain)
            ? RealTopLevelDomainConfidence
            : UnrecognisedTopLevelDomainConfidence;
    }

    /// <summary>Both <c>example.com</c> itself and anything under it: <c>mail.example.com</c> is nobody too.</summary>
    private static bool IsReservedForDocumentation(string domain) =>
        ReservedForDocumentation.Any(reserved =>
            domain.Equals(reserved, StringComparison.OrdinalIgnoreCase)
            || domain.EndsWith($".{reserved}", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Something, an at-sign, a dotted domain. Deliberately looser than the grammar an address is
    /// allowed by — what an address is <i>allowed</i> to look like is a page of RFC and matches things
    /// nobody has ever sent mail to; the domain is what decides here, so this only has to find one.
    /// </summary>
    [GeneratedRegex(
        @"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@(?<domain>[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)(?![A-Za-z0-9.-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailShapedText();
}
