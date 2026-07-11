using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// International bank account numbers. The shape of an IBAN — two letters, two digits, then a run of
/// alphanumerics — describes half the reference numbers in the average spreadsheet, so the shape only
/// nominates a candidate and the mod-97 checksum decides. Roughly one string in a hundred passes that
/// check by luck, which is why the country code gets the last word on how sure we are.
/// </summary>
public sealed partial class IbanDetector : IPiiDetector
{
    /// <summary>Country and length agree with the register, and the checksum holds. As close to certain as this gets.</summary>
    private const double KnownCountryConfidence = 0.95;

    /// <summary>
    /// The checksum holds, but no country issues account numbers under that code — most likely a
    /// reference number that got lucky. Reported anyway, because a missed IBAN costs more than a
    /// second look, but reported as the coin flip it is.
    /// </summary>
    private const double UnknownCountryConfidence = 0.6;

    private const int ShortestPossibleIban = 15;
    private const int LongestPossibleIban = 34;

    /// <summary>
    /// How long an IBAN is in each country that issues them. This is both the filter and the tell: a
    /// Polish IBAN is twenty-eight characters, and a twenty-seven character one is not a Polish IBAN.
    /// </summary>
    private static readonly Dictionary<string, int> IbanLengthByCountry = new(StringComparer.Ordinal)
    {
        ["AT"] = 20, ["BE"] = 16, ["BG"] = 22, ["CH"] = 21, ["CY"] = 28, ["CZ"] = 24,
        ["DE"] = 22, ["DK"] = 18, ["EE"] = 20, ["ES"] = 24, ["FI"] = 18, ["FR"] = 27,
        ["GB"] = 22, ["GR"] = 27, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IS"] = 26,
        ["IT"] = 27, ["LI"] = 21, ["LT"] = 20, ["LU"] = 20, ["LV"] = 21, ["MT"] = 31,
        ["NL"] = 18, ["NO"] = 15, ["PL"] = 28, ["PT"] = 25, ["RO"] = 24, ["SE"] = 24,
        ["SI"] = 19, ["SK"] = 24, ["SM"] = 27
    };

    public string Name => "IBAN";

    public PiiCategory Category => PiiCategory.Financial;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;

        foreach (Match candidateStart in IbanPrefix().Matches(text))
        {
            double candidateConfidence = ScoreCandidateAt(text, candidateStart.Index);

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

    /// <summary>How sure we are that an account number starts here — zero when one does not.</summary>
    private static double ScoreCandidateAt(string text, int startIndex)
    {
        string countryCode = text.Substring(startIndex, 2);

        if (IbanLengthByCountry.TryGetValue(countryCode, out int expectedLength))
        {
            string? candidate = ReadExactly(text, startIndex, expectedLength);

            return candidate is not null && HasValidChecksum(candidate) ? KnownCountryConfidence : 0;
        }

        string? unknownCountryCandidate = ReadUnspacedRun(text, startIndex);

        return unknownCountryCandidate is not null && HasValidChecksum(unknownCountryCandidate)
            ? UnknownCountryConfidence
            : 0;
    }

    /// <summary>
    /// Reads exactly the characters the country code promises, stepping over the single spaces an
    /// IBAN is as often as not written with ("PL61 1090 1014 …"). Null when the text runs out early,
    /// when something that is not part of an account number gets in the way, or when the run carries
    /// on past where the country says it should end — a longer string that merely starts like an IBAN
    /// is not one.
    /// </summary>
    private static string? ReadExactly(string text, int startIndex, int expectedLength)
    {
        var iban = new StringBuilder(expectedLength);
        int index = startIndex;

        while (index < text.Length && iban.Length < expectedLength)
        {
            char character = text[index];

            if (IsIbanCharacter(character))
            {
                iban.Append(character);
                index++;
                continue;
            }

            if (character == ' ' && index + 1 < text.Length && IsIbanCharacter(text[index + 1]))
            {
                index++;
                continue;
            }

            break;
        }

        return iban.Length == expectedLength && EndsHere(text, index)
            ? iban.ToString()
            : null;
    }

    /// <summary>
    /// The run of account-number characters starting here, spaces not allowed: with no country to say
    /// how long the number should be, a space is as likely to be the end of it as a break inside it.
    /// </summary>
    private static string? ReadUnspacedRun(string text, int startIndex)
    {
        int endIndex = startIndex;

        while (endIndex < text.Length && IsIbanCharacter(text[endIndex]))
        {
            endIndex++;
        }

        int length = endIndex - startIndex;

        return length is >= ShortestPossibleIban and <= LongestPossibleIban && EndsHere(text, endIndex)
            ? text[startIndex..endIndex]
            : null;
    }

    /// <summary>
    /// ISO 7064 mod-97: move the country code and the check digits to the back, read each letter as
    /// its place in the alphabet plus nine, and the whole thing — read as one number — must leave a
    /// remainder of 1 when divided by 97. That number runs to forty digits, so it is divided down as
    /// it is read rather than assembled.
    /// </summary>
    private static bool HasValidChecksum(string iban)
    {
        int remainder = 0;

        foreach (char character in string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4)))
        {
            int value = char.IsAsciiDigit(character)
                ? character - '0'
                : character - 'A' + 10;

            remainder = value > 9
                ? ((remainder * 100) + value) % 97
                : ((remainder * 10) + value) % 97;
        }

        return remainder == 1;
    }

    /// <summary>Whether the account number is allowed to stop here, or the text runs straight on into a longer word.</summary>
    private static bool EndsHere(string text, int index) =>
        index >= text.Length || !char.IsLetterOrDigit(text[index]);

    private static bool IsIbanCharacter(char character) =>
        char.IsAsciiDigit(character) || char.IsAsciiLetterUpper(character);

    /// <summary>
    /// Where an account number could begin: a country code and two check digits, on a word boundary.
    /// Everything after this is counted out by hand, because how far an IBAN runs depends on which
    /// country issued it — and a regex that guesses at the end of one swallows the next word instead.
    /// </summary>
    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex IbanPrefix();
}
