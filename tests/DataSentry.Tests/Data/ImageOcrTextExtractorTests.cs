using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using DataSentry.Data.TextExtraction;

namespace DataSentry.Tests.Data;

/// <summary>
/// The image fixtures are rendered here rather than checked in as binaries, so that what each
/// picture says is readable in the test that asserts on it.
/// </summary>
[TestFixture]
public class ImageOcrTextExtractorTests
{
    private const int SampleSizeCharacters = 4096;

    private string _rootPath = string.Empty;
    private OcrEngine _ocrEngine = null!;
    private ImageOcrTextExtractor _extractor = null!;

    [SetUp]
    public void SetUp()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"datasentry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
        _ocrEngine = new OcrEngine();
        _extractor = new ImageOcrTextExtractor(_ocrEngine);
    }

    [TearDown]
    public void TearDown()
    {
        _ocrEngine.Dispose();
        Directory.Delete(_rootPath, recursive: true);
    }

    [TestCase(".png")]
    [TestCase(".jpg")]
    [TestCase(".jpeg")]
    [TestCase(".tif")]
    [TestCase(".bmp")]
    public void ImageOcrExtractor_ImageExtension_ClaimsIt(string extension)
    {
        Assert.That(_extractor.CanExtract(extension), Is.True);
    }

    [TestCase(".pdf")]
    [TestCase(".txt")]
    [TestCase("")]
    public void ImageOcrExtractor_AnythingElse_LeavesItAlone(string extension)
    {
        Assert.That(_extractor.CanExtract(extension), Is.False);
    }

    [Test]
    public async Task ExtractText_ScannedDocument_ReturnsThePrintedText()
    {
        string filePath = GivenImageSaying("Contact: jan.kowalski@example.com");

        string? text = await _extractor.ExtractTextAsync(filePath, SampleSizeCharacters);

        Assert.That(text, Does.Contain("example.com").IgnoreCase,
            "an email address printed on a scan must reach the PII detectors as text");
    }

    [Test]
    public async Task ExtractText_ImageLongerThanTheLimit_StopsAtTheSampleSize()
    {
        string filePath = GivenImageSaying("A document with far more words than the sample allows");

        string? text = await _extractor.ExtractTextAsync(filePath, maxCharacters: 8);

        Assert.That(text, Has.Length.EqualTo(8));
    }

    [Test]
    public async Task ExtractText_BlankImage_ReturnsNull()
    {
        string filePath = Path.Combine(_rootPath, "blank.png");
        using (var blank = new Bitmap(400, 200))
        using (Graphics canvas = Graphics.FromImage(blank))
        {
            canvas.Clear(Color.White);
            blank.Save(filePath, ImageFormat.Png);
        }

        string? text = await _extractor.ExtractTextAsync(filePath, SampleSizeCharacters);

        Assert.That(text, Is.Null, "a picture with no words on it holds no text to sample");
    }

    [Test]
    public void ExtractText_CorruptImage_ThrowsSoTheScanCanRecordIt()
    {
        string filePath = Path.Combine(_rootPath, "corrupt.png");
        File.WriteAllText(filePath, "this is not an image");

        Assert.That(
            async () => await _extractor.ExtractTextAsync(filePath, SampleSizeCharacters),
            Throws.Exception,
            "the scan engine turns this into a ScanError; it is not the extractor's job to swallow it");
    }

    /// <summary>Black text on a white background at print size — what a scanner produces on a good day.</summary>
    private string GivenImageSaying(string printedText)
    {
        string filePath = Path.Combine(_rootPath, "scan.png");

        using var scan = new Bitmap(1600, 300);
        using (Graphics canvas = Graphics.FromImage(scan))
        using (var font = new Font("Arial", 32))
        {
            canvas.Clear(Color.White);
            canvas.DrawString(printedText, font, Brushes.Black, new PointF(40, 100));
        }

        scan.Save(filePath, ImageFormat.Png);

        return filePath;
    }
}
