using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// Payment card numbers: thirteen to nineteen digits, written as often with spaces or hyphens as
/// without, and validated by the Luhn check digit that every issuer computes them against.
/// </summary>
/// <remarks>
/// Luhn is a typo check, not an identity check — it catches a mistyped digit and nothing else, and one
/// number in ten passes it by accident. Worse, the numbers it passes by accident are exactly the ones a
/// spreadsheet is full of: a column of zeroes, a run of counting digits. Those go first, before Luhn is
/// ever consulted, and what is left is scored on whether any issuer alive actually hands out numbers
/// that start that way and run that long. The issuer decides how sure we are, and is then forgotten —
/// naming it in a finding would mean carrying the digits around to justify the name, and the digits are
/// the one thing that never leaves this method.
/// </remarks>
public sealed partial class PaymentCardDetector : IPiiDetector
{
    /// <summary>Luhn holds, and the number begins and ends the way a real issuer's numbers do.</summary>
    private const double KnownIssuerConfidence = 0.95;

    /// <summary>
    /// Luhn holds, but no issuer hands out numbers of this shape. Most likely a long reference number
    /// that got lucky — reported anyway, because a missed card number costs more than a second look.
    /// </summary>
    private const double UnknownIssuerConfidence = 0.6;

    public string Name => "payment card";

    public PiiCategory Category => PiiCategory.Financial;

    public PiiFinding? Detect(string text)
    {
        int matchCount = 0;
        double confidence = 0;
        var snippets = new List<string>();

        foreach (Match candidate in CardShapedRun().Matches(text))
        {
            double candidateConfidence = Score(Digits(candidate.Value));

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

    /// <summary>How sure we are that these digits are somebody's card — zero when they are not.</summary>
    private static double Score(string digits)
    {
        if (IsAllTheSameDigit(digits) || IsCountingUpOrDown(digits) || !HasValidLuhnCheckDigit(digits))
        {
            return 0;
        }

        return IssuerExists(digits) ? KnownIssuerConfidence : UnknownIssuerConfidence;
    }

    /// <summary>
    /// Whether a real issuer hands out numbers that start with these digits and run to this length.
    /// Consulted for the score and never recorded: a finding says how many cards, never whose or which.
    /// </summary>
    private static bool IssuerExists(string digits)
    {
        int length = digits.Length;
        int firstTwo = int.Parse(digits.AsSpan(0, 2));
        int firstFour = int.Parse(digits.AsSpan(0, 4));

        return digits[0] switch
        {
            '4' => length is 13 or 16 or 19,                                        // Visa
            '5' => firstTwo is >= 51 and <= 55 && length is 16,                     // Mastercard
            '2' => firstFour is >= 2221 and <= 2720 && length is 16,                // Mastercard, the newer range
            '3' => (firstTwo is 34 or 37 && length is 15)                           // American Express
                || (firstTwo is 36 or 38 && length is 14)                           // Diners Club
                || (firstTwo is 35 && length is 16),                                // JCB
            '6' => (firstFour is 6011 || firstTwo is 65) && length is 16,           // Discover
            _ => false
        };
    }

    /// <summary>A column of zeroes passes Luhn, and every issuer would like a word about that.</summary>
    private static bool IsAllTheSameDigit(string digits) =>
        digits.All(digit => digit == digits[0]);

    /// <summary>
    /// 1234567890123452 passes Luhn too. Nobody's card counts up, so a run where every digit is one
    /// more — or one less — than the digit before it is a placeholder, whatever the check digit says.
    /// </summary>
    private static bool IsCountingUpOrDown(string digits)
    {
        bool countingUp = true;
        bool countingDown = true;

        for (int position = 1; position < digits.Length; position++)
        {
            int step = digits[position] - digits[position - 1];

            countingUp &= step is 1 or -9;
            countingDown &= step is -1 or 9;
        }

        return countingUp || countingDown;
    }

    /// <summary>
    /// Luhn: double every second digit from the right, subtract nine from anything that runs into two
    /// digits, and the total must come out a multiple of ten.
    /// </summary>
    private static bool HasValidLuhnCheckDigit(string digits)
    {
        int total = 0;

        for (int position = 0; position < digits.Length; position++)
        {
            int digit = digits[^(position + 1)] - '0';

            if (position % 2 == 1)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            total += digit;
        }

        return total % 10 == 0;
    }

    private static string Digits(string candidate) =>
        new([.. candidate.Where(char.IsAsciiDigit)]);

    /// <summary>
    /// Thirteen to nineteen digits, grouped by single spaces or hyphens as a card is usually written,
    /// and standing on their own. The run has to be the whole of what is there: the first sixteen
    /// digits of a longer number are not a card number, and neither are sixteen of the twenty-six
    /// digits in a spaced-out account number, which is why a digit and a separator on either side of
    /// the match rule it out as firmly as a digit does.
    /// </summary>
    [GeneratedRegex(
        @"(?<![0-9A-Za-z])(?<![0-9][ -])[0-9](?:[ -]?[0-9]){12,18}(?![ -]?[0-9])(?![A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex CardShapedRun();
}
