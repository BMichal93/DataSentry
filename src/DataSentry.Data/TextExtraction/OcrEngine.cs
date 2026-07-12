using System;
using System.IO;
using Tesseract;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// The one Tesseract engine the whole application shares. OCR is by far the most expensive extraction
/// this project does, so the engine is created once, lazily, and every extractor that needs words read
/// off pixels — a photographed ID, a PDF page that holds a picture of a form instead of its text —
/// comes through here.
/// </summary>
/// <remarks>
/// The recognized text is handed back to the caller, who hands it to the detectors like any other
/// sample. It goes nowhere else — never logged, never stored.
/// </remarks>
public sealed class OcrEngine : IDisposable
{
    /// <summary>
    /// Below this, the "text" is the engine hallucinating letters into photo noise. Feeding that to
    /// the detectors buys false positives and nothing else.
    /// </summary>
    private const float MinimumMeanConfidence = 0.40f;

    private readonly Lazy<TesseractEngine> _engine = new(CreateEngine);

    /// <summary>A Tesseract engine instance processes one image at a time; this enforces that.</summary>
    private readonly object _engineLock = new();

    /// <summary>The words printed on the image, or null when there are none worth trusting.</summary>
    public string? RecognizeText(Pix image)
    {
        lock (_engineLock)
        {
            using Page recognized = _engine.Value.Process(image);

            if (recognized.GetMeanConfidence() < MinimumMeanConfidence)
            {
                return null;
            }

            string text = recognized.GetText();

            return string.IsNullOrWhiteSpace(text) ? null : text;
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
