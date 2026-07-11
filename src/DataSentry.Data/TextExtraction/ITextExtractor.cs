using System.Threading;
using System.Threading.Tasks;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// Pulls readable text out of one family of file formats, so that a PII detector can be run over a
/// spreadsheet without knowing what a spreadsheet is.
/// </summary>
/// <remarks>
/// This abstraction lives in <c>DataSentry.Data</c>, not in <c>Core</c>: parsing a file format is
/// data access, and Core is only ever handed the text that comes out. Core has no caller for a single
/// extractor — it asks <see cref="Core.Abstractions.IFileContentReader"/> for the text of a file and
/// does not care which format it was locked inside.
/// </remarks>
public interface ITextExtractor
{
    /// <summary>Whether this extractor handles the given extension, lowercased and including the dot.</summary>
    bool CanExtract(string extension);

    /// <summary>
    /// The file's text, cut off at <paramref name="maxCharacters"/> — a scan samples a file, it does
    /// not read a two-gigabyte export end to end.
    /// </summary>
    /// <returns>The text, or null when the file holds no text to sample.</returns>
    Task<string?> ExtractTextAsync(string filePath, int maxCharacters, CancellationToken cancellationToken = default);
}
