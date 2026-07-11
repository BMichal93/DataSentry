using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// Phone numbers, Polish and European. A number written the way you would dial it from abroad —
/// <c>+48 601 234 567</c> — announces itself; a bare <c>601 234 567</c> in a spreadsheet cell is nine
/// digits, and nine digits is also an order number, a postal code with the space in the wrong place,
/// and a part number. Both are reported, and the difference between them is the confidence.
/// </summary>
/// <remarks>
/// There is no checksum in a phone number, so the calling code is the only thing that can cut a false
/// positive — the same job the country code does in an IBAN. A number that names the country it is in
/// is a number; a run of digits that merely could be one is a guess, and is scored as a guess.
/// </remarks>
public sealed partial class PhoneNumberDetector : IPiiDetector
{
    /// <summary>Written internationally, under a calling code somebody actually answers the phone in.</summary>
    private const double KnownCallingCodeConfidence = 0.9;

    /// <summary>Written internationally, but under a calling code no country in Europe uses.</summary>
    private const double UnknownCallingCodeConfidence = 0.6;

    /// <summary>
    /// Nine digits, grouped the way a Polish number is grouped, and nothing else to go on. Reported —
    /// a phone book is still personal data — but never mistaken for certainty.
    /// </summary>
    private const double NationalFormatConfidence = 0.55;

    private const int ShortestSubscriberNumber = 6;
    private const int LongestSubscriberNumber = 12;

    /// <summary>The calling codes of Europe. A number under one of these is a number somebody answers.</summary>
    private static readonly HashSet<string> EuropeanCallingCodes = new(StringComparer.Ordinal)
    {
        "30", "31", "32", "33", "34", "36", "39", "40", "41", "43", "44", "45", "46", "47",
        "48", "49", "350", "351", "352", "353", "354", "355", "356", "357", "358", "359",
        "370", "371", "372", "373", "374", "375", "376", "377", "378", "379", "380", "381",
        "385", "386", "387", "389", "420", "421", "423"
    };

    public string Name => "phone number";

    public PiiCategory Category => PiiCategory.Contact;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;

        foreach (Match candidate in PhoneShapedRun().Matches(text))
        {
            double candidateConfidence = candidate.Groups["international"].Success
                ? ScoreInternational(Digits(candidate.Value))
                : NationalFormatConfidence;

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

    /// <summary>
    /// How sure we are that a number written with a calling code is a phone number. Zero when what
    /// follows the calling code is too short or too long to be anybody's line.
    /// </summary>
    private static double ScoreInternational(string digits)
    {
        foreach (int callingCodeLength in new[] { 3, 2 })
        {
            if (digits.Length <= callingCodeLength || !EuropeanCallingCodes.Contains(digits[..callingCodeLength]))
            {
                continue;
            }

            return IsPlausibleSubscriberNumber(digits.Length - callingCodeLength)
                ? KnownCallingCodeConfidence
                : 0;
        }

        // Somewhere outside Europe, or nowhere at all. Either way the calling code cannot vouch for it.
        return IsPlausibleSubscriberNumber(digits.Length - 2) ? UnknownCallingCodeConfidence : 0;
    }

    private static bool IsPlausibleSubscriberNumber(int digitCount) =>
        digitCount is >= ShortestSubscriberNumber and <= LongestSubscriberNumber;

    private static string Digits(string candidate) =>
        new([.. candidate.Where(char.IsAsciiDigit)]);

    /// <summary>
    /// Either form, in one pass and in this order, so that the nine digits inside <c>+48 601 234 567</c>
    /// are read as the tail of an international number and not counted a second time as a national one.
    /// Both forms have to be the whole of what is written: a run of digits that carries on past where a
    /// phone number ends was never a phone number, so another digit group on either side rules it out.
    /// </summary>
    [GeneratedRegex(
        """
        (?<international>\+[0-9](?:[ -]?[0-9]){6,14}(?![ -]?[0-9]))
        | (?<![0-9+])(?<![0-9][ -])[0-9]{3}[ -]?[0-9]{3}[ -]?[0-9]{3}(?![ -]?[0-9])
        """,
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex PhoneShapedRun();
}
