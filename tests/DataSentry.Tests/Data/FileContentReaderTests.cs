using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Data.FileSystem;
using DataSentry.Data.TextExtraction;

namespace DataSentry.Tests.Data;

[TestFixture]
public class FileContentReaderTests
{
    private const int SampleSizeBytes = 4096;

    private string _rootPath = string.Empty;
    private IFileContentReader _contentReader = null!;

    [SetUp]
    public void SetUp()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"datasentry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _contentReader = new FileContentReader(
            [new SpreadsheetTextExtractor(), new WordDocumentTextExtractor(), new PdfTextExtractor()],
            new PlainTextExtractor());
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_rootPath, recursive: true);
    }

    [Test]
    public async Task ReadTextSample_TextFile_ReturnsItsContent()
    {
        string filePath = GivenFile("notes.txt", Encoding.UTF8.GetBytes("Invoice 2019"));

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.EqualTo("Invoice 2019"));
    }

    [Test]
    public async Task ReadTextSample_EmptyFile_ReturnsAnEmptySample()
    {
        string filePath = GivenFile("empty.csv", []);

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.Empty);
    }

    [Test]
    public async Task ReadTextSample_FileLargerThanTheLimit_ReadsOnlyTheFirstBytes()
    {
        string filePath = GivenFile("export.csv", Encoding.UTF8.GetBytes(new string('a', 10_000)));

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, maxCharacters: 64);

        Assert.That(sample, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task ReadTextSample_BinaryFile_ReturnsNullSoDetectorsDoNotRunOverIt()
    {
        string filePath = GivenFile("photo.jpg", [0xFF, 0xD8, 0xFF, 0x00, 0x10, 0x4A]);

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.Null);
    }

    [Test]
    public async Task ReadTextSample_Utf16File_IsDecodedRatherThanMistakenForBinary()
    {
        byte[] contents = [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes("Jan Kowalski")];
        string filePath = GivenFile("names.txt", contents);

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.EqualTo("Jan Kowalski"));
    }

    [Test]
    public async Task ReadTextSample_Utf8FileWithByteOrderMark_DropsTheMark()
    {
        byte[] contents = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes("email@example.com")];
        string filePath = GivenFile("contacts.csv", contents);

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.EqualTo("email@example.com"));
    }

    [Test]
    public async Task ReadTextSample_FileLockedByAnotherProcess_IsStillReadable()
    {
        string filePath = GivenFile("in-use.csv", Encoding.UTF8.GetBytes("open in Excel"));

        using FileStream _ = new(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.EqualTo("open in Excel"));
    }

    [Test]
    public async Task ReadTextSample_UnknownExtensionHoldingText_FallsBackToReadingItAsText()
    {
        string filePath = GivenFile("export.dat", Encoding.UTF8.GetBytes("jan@example.com"));

        string? sample = await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes);

        Assert.That(sample, Is.EqualTo("jan@example.com"));
    }

    [Test]
    public void ReadTextSample_SpreadsheetExtension_IsRoutedToTheSpreadsheetExtractor()
    {
        // A .xlsx is a zip archive, so the plain text extractor would sniff binary and return null.
        // Getting a parse failure here instead proves the file reached the right extractor.
        string filePath = GivenFile("payroll.xlsx", Encoding.UTF8.GetBytes("not really a workbook"));

        Assert.That(
            async () => await _contentReader.ReadTextSampleAsync(filePath, SampleSizeBytes),
            Throws.Exception);
    }

    [Test]
    public void ReadTextSample_MissingFile_Throws()
    {
        string missingPath = Path.Combine(_rootPath, "gone.txt");

        Assert.That(
            async () => await _contentReader.ReadTextSampleAsync(missingPath, SampleSizeBytes),
            Throws.InstanceOf<FileNotFoundException>());
    }

    [Test]
    public async Task ComputeContentHash_IdenticalContent_ProducesTheSameHash()
    {
        string firstPath = GivenFile("a.txt", Encoding.UTF8.GetBytes("duplicate"));
        string secondPath = GivenFile("b.txt", Encoding.UTF8.GetBytes("duplicate"));

        string firstHash = await _contentReader.ComputeContentHashAsync(firstPath);
        string secondHash = await _contentReader.ComputeContentHashAsync(secondPath);

        Assert.That(firstHash, Is.EqualTo(secondHash));
    }

    [Test]
    public async Task ComputeContentHash_DifferentContent_ProducesDifferentHashes()
    {
        string firstPath = GivenFile("a.txt", Encoding.UTF8.GetBytes("original"));
        string secondPath = GivenFile("b.txt", Encoding.UTF8.GetBytes("different"));

        string firstHash = await _contentReader.ComputeContentHashAsync(firstPath);
        string secondHash = await _contentReader.ComputeContentHashAsync(secondPath);

        Assert.That(firstHash, Is.Not.EqualTo(secondHash));
    }

    [Test]
    public async Task ComputeContentHash_EmptyFile_ProducesTheHashOfNothing()
    {
        string filePath = GivenFile("empty.txt", []);

        string hash = await _contentReader.ComputeContentHashAsync(filePath);

        Assert.That(hash, Is.EqualTo("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
    }

    private string GivenFile(string fileName, byte[] contents)
    {
        string filePath = Path.Combine(_rootPath, fileName);
        File.WriteAllBytes(filePath, contents);

        return filePath;
    }
}
