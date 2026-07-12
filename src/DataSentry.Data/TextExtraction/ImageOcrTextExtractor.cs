using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// Reads the words printed <em>on</em> an image — a scanned passport, a photographed invoice, an ID
/// dropped into a shared drive as a JPEG. Without OCR these files hold no text at all, and the PII
/// most worth finding is exactly the kind that arrives as a scan.
/// </summary>
public sealed class ImageOcrTextExtractor : ITextExtractor
{
    /// <summary>
    /// Larger than this and the file is footage or raw camera output, not a scanned document — and
    /// OCR time grows with pixels, so the scan is better spent on the next thousand files.
    /// </summary>
    private const long MaxImageSizeBytes = 50 * 1024 * 1024;

    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif"];

    private readonly OcrEngine _ocrEngine;

    public ImageOcrTextExtractor(OcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
    }

    public bool CanExtract(string extension) => SupportedExtensions.Contains(extension);

    public Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(filePath, maxCharacters, cancellationToken), cancellationToken);

    private string? ExtractText(string filePath, int maxCharacters, CancellationToken cancellationToken)
    {
        if (new FileInfo(filePath).Length > MaxImageSizeBytes)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using Pix image = Pix.LoadFromFile(filePath);

        string? text = _ocrEngine.RecognizeText(image);

        return text is null ? null : TextSample.Truncate(text, maxCharacters);
    }
}
