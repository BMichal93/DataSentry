using System.Text;

namespace DataSentry.Data.TextExtraction;

/// <summary>Keeps every extractor honest about the sample limit it was given.</summary>
internal static class TextSample
{
    public static string Truncate(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters];

    /// <summary>
    /// Appends a fragment of the document, and says whether the sample is now full. An extractor that
    /// keeps parsing after this returns true is reading a whole file to throw most of it away.
    /// </summary>
    public static bool AppendUntilFull(StringBuilder sample, string? fragment, int maxCharacters)
    {
        if (!string.IsNullOrEmpty(fragment))
        {
            sample.Append(fragment);
            sample.Append(' ');
        }

        return sample.Length >= maxCharacters;
    }
}
