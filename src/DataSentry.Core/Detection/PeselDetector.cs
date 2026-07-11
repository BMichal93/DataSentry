using System.Globalization;
using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// The Polish national identification number: eleven digits, of which the first six are a birth date
/// and the last is a weighted checksum. Eleven digits in a row is also an order number, a phone number
/// with the country code stuck to it, and half the reference columns in an export — so the shape only
/// nominates a candidate, and two things have to agree before it is called a PESEL.
/// </summary>
/// <remarks>
/// The checksum alone is not enough: one eleven-digit number in ten passes it by luck. The birth date
/// is what settles it, because a number cannot carry a date that never happened. A valid checksum over
/// the thirteenth month is not a PESEL, and the person born in 2247 that the century digits sometimes
/// describe has not been born yet.
/// </remarks>
public sealed partial class PeselDetector : IPiiDetector
{
    /// <summary>Checksum holds and the number carries a birth date somebody alive could have.</summary>
    private const double LivingPersonConfidence = 0.95;

    /// <summary>
    /// Checksum holds and the date is real, but it falls in the nineteenth century — the encoding
    /// allows it, and it is far likelier to be an eleven-digit reference number that got lucky than a
    /// customer born in 1885. Reported, but as the coin flip it is.
    /// </summary>
    private const double ImplausiblyOldConfidence = 0.6;

    /// <summary>The weights the check digit is computed against, in order, for the first ten digits.</summary>
    private static readonly int[] CheckDigitWeights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];

    /// <summary>
    /// The month carries the century: 1–12 is the 1900s, and each further block of twenty steps a
    /// hundred years — 21–32 the 2000s, 41–52 the 2100s, 61–72 the 2200s, and 81–92 back to the 1800s.
    /// </summary>
    private static readonly int[] CenturyByMonthBlock = [1900, 2000, 2100, 2200, 1800];

    public string Name => "PESEL";

    public PiiCategory Category => PiiCategory.Identity;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;

        foreach (Match candidate in ElevenDigits().Matches(text))
        {
            double candidateConfidence = Score(candidate.Value);

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

    /// <summary>How sure we are that these eleven digits identify a person — zero when they do not.</summary>
    private static double Score(string digits)
    {
        if (!HasValidCheckDigit(digits))
        {
            return 0;
        }

        DateOnly? birthDate = DecodeBirthDate(digits);

        if (birthDate is null || birthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return 0;
        }

        return birthDate.Value.Year < 1900 ? ImplausiblyOldConfidence : LivingPersonConfidence;
    }

    /// <summary>
    /// The birth date the first six digits describe, or null when they describe no date at all — the
    /// thirtieth of February, the thirteenth month, a century block that does not exist.
    /// </summary>
    private static DateOnly? DecodeBirthDate(string digits)
    {
        int yearInCentury = Number(digits, 0, 2);
        int encodedMonth = Number(digits, 2, 2);
        int day = Number(digits, 4, 2);

        int centuryBlock = encodedMonth / 20;
        int month = encodedMonth % 20;

        if (centuryBlock >= CenturyByMonthBlock.Length || month is < 1 or > 12)
        {
            return null;
        }

        int year = CenturyByMonthBlock[centuryBlock] + yearInCentury;

        return day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
    }

    /// <summary>
    /// Each of the first ten digits is multiplied by its weight, and the last digit is whatever it
    /// takes to bring the total up to a multiple of ten.
    /// </summary>
    private static bool HasValidCheckDigit(string digits)
    {
        int weightedSum = 0;

        for (int position = 0; position < CheckDigitWeights.Length; position++)
        {
            weightedSum += (digits[position] - '0') * CheckDigitWeights[position];
        }

        int expectedCheckDigit = (10 - (weightedSum % 10)) % 10;

        return digits[10] - '0' == expectedCheckDigit;
    }

    private static int Number(string digits, int startIndex, int length) =>
        int.Parse(digits.AsSpan(startIndex, length), CultureInfo.InvariantCulture);

    /// <summary>
    /// Eleven digits standing on their own. Not the first eleven of a longer run — a sixteen-digit card
    /// number contains an eleven-digit number and is not a PESEL — and not the eleven digits of a phone
    /// number with its country code attached, which is what the plus sign rules out.
    /// </summary>
    [GeneratedRegex(@"(?<![0-9+])[0-9]{11}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex ElevenDigits();
}
