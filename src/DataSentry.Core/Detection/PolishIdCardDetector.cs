using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// The Polish national identity card number ("seria i numer dowodu osobistego"): three letters, six
/// digits, of which the first digit is a weighted checksum over everything else. Three letters and six
/// digits is also a booking code, a licence plate with the space dropped, and a shipment reference —
/// so the shape only nominates a candidate, and the checksum decides.
/// </summary>
/// <remarks>
/// This is the number a photocopied or scanned ID carries in plain sight, which makes it the ID most
/// likely to be found by OCR: PESEL fields on forms are usually printed in per-digit boxes that OCR
/// mangles, while the document number is set as one unbroken string.
/// </remarks>
public sealed partial class PolishIdCardDetector : IPiiDetector
{
    /// <summary>
    /// The checksum holds. One shapely candidate in ten passes by luck, so this stays a step below
    /// the detectors that can also cross-check a birth date or a country's length register.
    /// </summary>
    private const double ChecksumConfidence = 0.9;

    /// <summary>
    /// Each character's value — a digit is itself, a letter is its place in the alphabet plus nine —
    /// multiplied by its weight, summed over all nine characters, must land on a multiple of ten.
    /// The fourth character is the check digit; its weight makes it part of its own equation.
    /// </summary>
    private static readonly int[] CharacterWeights = [7, 3, 1, 9, 7, 3, 1, 7, 3];

    public string Name => "Polish ID card number";

    public PiiCategory Category => PiiCategory.Identity;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;

        foreach (Match candidate in ThreeLettersSixDigits().Matches(text))
        {
            if (HasValidChecksum(candidate.Value.Replace(" ", "")))
            {
                matchCount++;
            }
        }

        return matchCount == 0
            ? null
            : new PiiFinding(Category, Name, matchCount, ChecksumConfidence);
    }

    private static bool HasValidChecksum(string documentNumber)
    {
        int weightedSum = 0;

        for (int position = 0; position < CharacterWeights.Length; position++)
        {
            char character = documentNumber[position];

            int value = char.IsAsciiDigit(character)
                ? character - '0'
                : character - 'A' + 10;

            weightedSum += value * CharacterWeights[position];
        }

        return weightedSum % 10 == 0;
    }

    /// <summary>
    /// Three capital letters and six digits standing on their own, written solid ("ABA598125") or with
    /// the customary space after the series ("ABA 598125"). Not a slice of something longer: a word
    /// running into the letters or a digit running out of the number disqualifies the candidate.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{3} ?[0-9]{6}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex ThreeLettersSixDigits();
}
