using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DataSentry.Data.TextExtraction;

/// <summary>Reads the text of a Word document (.docx).</summary>
public sealed class WordDocumentTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension is ".docx" or ".docm";

    public Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default) =>
        // OpenXml is a synchronous library, and a scan must never be the reason the window stops
        // repainting.
        Task.Run(() => ExtractText(filePath, maxCharacters, cancellationToken), cancellationToken);

    private static string? ExtractText(string filePath, int maxCharacters, CancellationToken cancellationToken)
    {
        using WordprocessingDocument document = WordprocessingDocument.Open(filePath, isEditable: false);

        MainDocumentPart? mainPart = document.MainDocumentPart;

        if (mainPart is null)
        {
            return null;
        }

        var sample = new StringBuilder(maxCharacters);

        // Streamed with a reader rather than InnerText over the whole body: a long report is read only
        // as far as the sample limit, not loaded into memory in full and then thrown away.
        using OpenXmlReader reader = OpenXmlReader.Create(mainPart);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.ElementType != typeof(Text))
            {
                continue;
            }

            if (TextSample.AppendUntilFull(sample, reader.GetText(), maxCharacters))
            {
                break;
            }
        }

        return TextSample.Truncate(sample.ToString(), maxCharacters);
    }
}
