using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSentry.Data.TextExtraction;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Document = DocumentFormat.OpenXml.Wordprocessing.Document;
using PageSize = UglyToad.PdfPig.Content.PageSize;
using Run = DocumentFormat.OpenXml.Wordprocessing.Run;
using SheetText = DocumentFormat.OpenXml.Spreadsheet.Text;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace DataSentry.Tests.Data;

/// <summary>
/// The fixtures are built here rather than checked in as binaries, so that what each document
/// contains is readable in the test that asserts on it.
/// </summary>
[TestFixture]
public class TextExtractorTests
{
    private const int SampleSizeCharacters = 4096;

    private string _rootPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"datasentry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_rootPath, recursive: true);
    }

    [TestCase(".xlsx")]
    [TestCase(".xlsm")]
    public void SpreadsheetExtractor_ExcelExtension_ClaimsIt(string extension)
    {
        Assert.That(new SpreadsheetTextExtractor().CanExtract(extension), Is.True);
    }

    [TestCase(".docx")]
    [TestCase(".csv")]
    [TestCase("")]
    public void SpreadsheetExtractor_AnythingElse_LeavesItAlone(string extension)
    {
        Assert.That(new SpreadsheetTextExtractor().CanExtract(extension), Is.False);
    }

    [Test]
    public async Task ExtractText_Spreadsheet_ReturnsTheCellValues()
    {
        string filePath = GivenSpreadsheet(
            sharedStrings: ["Jan Kowalski", "jan@example.com"],
            numericValue: "1234");

        string? text = await new SpreadsheetTextExtractor().ExtractTextAsync(filePath, SampleSizeCharacters);

        Assert.That(text, Does.Contain("Jan Kowalski"));
        Assert.That(text, Does.Contain("jan@example.com"), "shared strings are resolved through the string table");
        Assert.That(text, Does.Contain("1234"), "a cell that is not a shared string still carries its value");
    }

    [Test]
    public async Task ExtractText_SpreadsheetLongerThanTheLimit_StopsAtTheSampleSize()
    {
        string filePath = GivenSpreadsheet(
            sharedStrings: [.. Enumerable.Range(0, 500).Select(row => $"row-{row}-padding-text")],
            numericValue: "1");

        string? text = await new SpreadsheetTextExtractor().ExtractTextAsync(filePath, maxCharacters: 64);

        Assert.That(text, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task ExtractText_WordDocument_ReturnsTheParagraphText()
    {
        string filePath = GivenWordDocument("Employee health record", "Diagnosis on file");

        string? text = await new WordDocumentTextExtractor().ExtractTextAsync(filePath, SampleSizeCharacters);

        Assert.That(text, Does.Contain("Employee health record"));
        Assert.That(text, Does.Contain("Diagnosis on file"));
    }

    [Test]
    public async Task ExtractText_WordDocumentLongerThanTheLimit_StopsAtTheSampleSize()
    {
        string filePath = GivenWordDocument([.. Enumerable.Range(0, 200).Select(index => $"paragraph {index}")]);

        string? text = await new WordDocumentTextExtractor().ExtractTextAsync(filePath, maxCharacters: 32);

        Assert.That(text, Has.Length.EqualTo(32));
    }

    [Test]
    public async Task ExtractText_Pdf_ReturnsThePageText()
    {
        string filePath = GivenPdf("Invoice PL61109010140000071219812874");

        string? text = await new PdfTextExtractor().ExtractTextAsync(filePath, SampleSizeCharacters);

        Assert.That(text, Does.Contain("PL61109010140000071219812874"));
    }

    [Test]
    public void ExtractText_CorruptDocument_ThrowsSoTheScanCanRecordIt()
    {
        string filePath = Path.Combine(_rootPath, "corrupt.xlsx");
        File.WriteAllText(filePath, "this is not a spreadsheet");

        Assert.That(
            async () => await new SpreadsheetTextExtractor().ExtractTextAsync(filePath, SampleSizeCharacters),
            Throws.Exception,
            "the scan engine turns this into a ScanError; it is not the extractor's job to swallow it");
    }

    private string GivenSpreadsheet(string[] sharedStrings, string numericValue)
    {
        string filePath = Path.Combine(_rootPath, "payroll.xlsx");

        using SpreadsheetDocument document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);

        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        SharedStringTablePart sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStringPart.SharedStringTable = new SharedStringTable(
            sharedStrings.Select(value => new SharedStringItem(new SheetText(value))));

        var sheetData = new SheetData();

        for (int index = 0; index < sharedStrings.Length; index++)
        {
            sheetData.Append(new Row(new Cell
            {
                DataType = CellValues.SharedString,
                CellValue = new CellValue(index.ToString())
            }));
        }

        sheetData.Append(new Row(new Cell { CellValue = new CellValue(numericValue) }));

        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        workbookPart.Workbook.AppendChild(new Sheets()).AppendChild(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Payroll"
        });

        workbookPart.Workbook.Save();

        return filePath;
    }

    private string GivenWordDocument(params string[] paragraphs)
    {
        string filePath = Path.Combine(_rootPath, "records.docx");

        using WordprocessingDocument document = WordprocessingDocument.Create(
            filePath,
            WordprocessingDocumentType.Document);

        MainDocumentPart mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(
            new Body(paragraphs.Select(paragraph => new Paragraph(new Run(new WordText(paragraph))))));

        mainPart.Document.Save();

        return filePath;
    }

    private string GivenPdf(string contents)
    {
        string filePath = Path.Combine(_rootPath, "invoice.pdf");

        var builder = new PdfDocumentBuilder();
        PdfPageBuilder page = builder.AddPage(PageSize.A4);
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

        page.AddText(contents, 12, new PdfPoint(25, 700), font);

        File.WriteAllBytes(filePath, builder.Build());

        return filePath;
    }
}
