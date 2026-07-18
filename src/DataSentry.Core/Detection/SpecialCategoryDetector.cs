using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// Data whose misuse does a person the most harm: health, biometrics, genetics, racial or ethnic
/// origin, political opinion, religious belief, trade union membership, sex life and orientation —
/// the special categories of GDPR Art. 9. A hit here decides the file on its own: it is the first
/// question <see cref="Classification.RecommendationPolicy"/> asks, and a file that answers yes goes
/// to a human, whatever every other rule in the tool has to say about it.
/// </summary>
/// <remarks>
/// There is nothing to validate — a word is either written down or it is not — so the only defence
/// against false positives is what the words are and how many of them turned up. One term could be a
/// figure of speech in a memo; three terms from the same list, in the same file, is a record about a
/// person, and the count is what the confidence is built on.
///
/// This is deliberately not the generic keyword category: keyword hits are noise scored Low, and Art. 9
/// data is the highest consequence in the tool. Folding one into the other would bury it.
/// </remarks>
public sealed class SpecialCategoryDetector : IPiiDetector
{
    /// <summary>Several different terms from the list, in one file. That is a record, not a turn of phrase.</summary>
    private const double CorroboratedConfidence = 0.9;

    /// <summary>A single term, once. Enough to look at — Art. 9 always is — and not enough to be sure.</summary>
    private const double SingleTermConfidence = 0.6;

    /// <summary>How many different terms it takes before the file is speaking about a person and not in passing.</summary>
    private const int CorroboratingTermCount = 2;

    private static readonly Regex Terms = BuildTermsRegex();

    public string Name => "special category term";

    public PiiCategory Category => PiiCategory.SpecialCategory;

    public PiiFinding? Detect(string text)
    {
        var distinctTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snippets = new List<string>();
        int matchCount = 0;

        foreach (Match match in Terms.Matches(text))
        {
            matchCount++;
            distinctTerms.Add(match.Value);
            snippets.Add(SnippetRedactor.Redact(match.Value));
        }

        if (matchCount == 0)
        {
            return null;
        }

        double confidence = distinctTerms.Count >= CorroboratingTermCount
            ? CorroboratedConfidence
            : SingleTermConfidence;

        return new PiiFinding(Category, Name, matchCount, confidence, snippets);
    }

    /// <summary>
    /// The whole term list as one alternation, so that a file is read once rather than once per term.
    /// The longer terms come first: "medical record" is a better answer than "record", and an
    /// alternation hands back whichever branch it reaches first.
    /// </summary>
    private static Regex BuildTermsRegex()
    {
        IEnumerable<string> terms = ReadTerms()
            .OrderByDescending(term => term.Length)
            .Select(Regex.Escape);

        return new Regex(
            $@"\b(?:{string.Join('|', terms)})\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> ReadTerms()
    {
        using Stream resource = typeof(SpecialCategoryDetector).Assembly.GetManifestResourceStream(
            "DataSentry.Core.Detection.Resources.SpecialCategoryTerms.txt")
            ?? throw new InvalidOperationException("The special category term list is missing from the assembly.");

        using var reader = new StreamReader(resource);

        while (reader.ReadLine() is { } line)
        {
            string term = line.Trim();

            if (term.Length > 0 && !term.StartsWith('#'))
            {
                yield return term;
            }
        }
    }
}
