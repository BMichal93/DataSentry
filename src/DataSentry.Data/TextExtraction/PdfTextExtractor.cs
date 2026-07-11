using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DataSentry.Data.TextExtraction;

/// <summary>Reads the text of a PDF. Scanned PDFs hold pictures of words, not words, and yield nothing.</summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension is ".pdf";

    public Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(filePath, maxCharacters, cancellationToken), cancellationToken);

    private static string? ExtractText(string filePath, int maxCharacters, CancellationToken cancellationToken)
    {
        using PdfDocument document = PdfDocument.Open(filePath);

        var sample = new StringBuilder(maxCharacters);

        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TextSample.AppendUntilFull(sample, page.Text, maxCharacters))
            {
                break;
            }
        }

        return TextSample.Truncate(sample.ToString(), maxCharacters);
    }
}
