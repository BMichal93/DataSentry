using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using UglyToad.PdfPig;
using Page = UglyToad.PdfPig.Content.Page;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// Reads the text of a PDF. Pages that carry a real text layer are read from it directly; a page that
/// holds a picture of words instead — a scanned declaration, a form printed to PDF with its letters
/// turned into vector outlines — is rendered to an image and read by OCR. Those are precisely the
/// FATCA forms and ID scans this tool exists to find, and a text layer is the one thing they never have.
/// </summary>
// The page renderer is native PDFium, which does not reach every platform .NET does. DataSentry is
// a Windows desktop tool, so saying so here is a statement of fact, not a restriction.
[SupportedOSPlatform("windows")]
public sealed class PdfTextExtractor : ITextExtractor
{
    /// <summary>
    /// Fewer letters than this and the page's text layer is not the page: a scanned A4 sheet yields
    /// zero letters, an ordinary one hundreds. The threshold only needs to sit in the wide gap between
    /// those two, and being generous costs at most one OCR pass over a page that is nearly blank.
    /// </summary>
    private const int MinimumLettersForTextLayer = 32;

    /// <summary>
    /// OCR costs seconds per page where the text layer costs nothing. The data that identifies a
    /// person sits on the first pages of a scanned form, so the pages past this cap are the part of
    /// the trade the sample can afford to give up.
    /// </summary>
    private const int MaxOcrPagesPerDocument = 8;

    /// <summary>What a page is rasterized at for OCR: the small print on a form needs this much.</summary>
    private const int OcrRenderDpi = 300;

    private readonly OcrEngine _ocrEngine;

    public PdfTextExtractor(OcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public bool CanExtract(string extension) => extension is ".pdf";

    public Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(filePath, maxCharacters, cancellationToken), cancellationToken);

    private string? ExtractText(string filePath, int maxCharacters, CancellationToken cancellationToken)
    {
        using PdfDocument document = PdfDocument.Open(filePath);

        var sample = new StringBuilder(maxCharacters);
        byte[]? renderableDocument = null;
        int pagesRecognized = 0;

        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? pageText = page.Text;

            if (NeedsOcr(page) && pagesRecognized < MaxOcrPagesPerDocument)
            {
                // Read once, lazily: most PDFs never reach this line, and the renderer wants the raw
                // bytes rather than the document PdfPig is holding open.
                renderableDocument ??= File.ReadAllBytes(filePath);

                pageText = RecognizePrintedPage(renderableDocument, page.Number) ?? page.Text;
                pagesRecognized++;
            }

            if (TextSample.AppendUntilFull(sample, pageText, maxCharacters))
            {
                break;
            }
        }

        return TextSample.Truncate(sample.ToString(), maxCharacters);
    }

    /// <summary>
    /// Whether the page's words are pixels or paths rather than text. A page with next to no letters
    /// but something drawn on it is a picture of a document; a page with nothing on it at all is just
    /// blank, and OCR would only be handed an empty sheet.
    /// </summary>
    private static bool NeedsOcr(Page page) =>
        page.Letters.Count < MinimumLettersForTextLayer
        && (page.Paths.Count > 0 || page.GetImages().Any());

    /// <summary>
    /// Renders the page to an in-memory image and reads the words off it. The image lives only for
    /// this call — like the text itself, it is never written anywhere.
    /// </summary>
    private string? RecognizePrintedPage(byte[] renderableDocument, int pageNumber)
    {
        using SKBitmap pageImage = Conversion.ToImage(
            renderableDocument,
            page: pageNumber - 1,
            options: new RenderOptions(Dpi: OcrRenderDpi));

        using SKData encodedPage = pageImage.Encode(SKEncodedImageFormat.Png, quality: 100);
        using Pix recognizablePage = Pix.LoadFromMemory(encodedPage.ToArray());

        return _ocrEngine.RecognizeText(recognizablePage);
    }
}
