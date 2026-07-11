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
/// <remarks>
/// OCR is by far the most expensive extraction this project does, so the engine is created once,
/// lazily, and oversized images are skipped outright. The recognized text is handed to the detectors
/// like any other sample and goes nowhere else — never logged, never stored.
/// </remarks>
public sealed class ImageOcrTextExtractor : ITextExtractor, IDisposable
{
    /// <summary>
    /// Larger than this and the file is footage or raw camera output, not a scanned document — and
    /// OCR time grows with pixels, so the scan is better spent on the next thousand files.
    /// </summary>
    private const long MaxImageSizeBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Below this, the "text" is the engine hallucinating letters into photo noise. Feeding that to
    /// the detectors buys false positives and nothing else.
    /// </summary>
    private const float MinimumMeanConfidence = 0.40f;

    private static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif"];

    private readonly Lazy<TesseractEngine> _engine = new(CreateEngine);

    /// <summary>A Tesseract engine instance processes one image at a time; this enforces that.</summary>
    private readonly object _engineLock = new();

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

        lock (_engineLock)
        {
            using Page recognized = _engine.Value.Process(image);

            if (recognized.GetMeanConfidence() < MinimumMeanConfidence)
            {
                return null;
            }

            string text = recognized.GetText();

            return string.IsNullOrWhiteSpace(text) ? null : TextSample.Truncate(text, maxCharacters);
        }
    }

    /// <summary>
    /// The language model ships in the build output next to the app. English covers the digits and
    /// Latin script that every detector here matches on — PESEL, IBAN, card and phone numbers, email
    /// addresses — including on documents whose surrounding prose is not English.
    /// </summary>
    private static TesseractEngine CreateEngine()
    {
        string tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");

        // The shipped model is from tessdata_fast, which only the LSTM engine can load.
        return new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly);
    }

    public void Dispose()
    {
        if (_engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }
    }
}
