using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// Reads the cell values of an Excel workbook (.xlsx). The spreadsheet nobody remembers keeping is the
/// file this whole tool exists to find, so this extractor earns its keep more than any other.
/// </summary>
public sealed class SpreadsheetTextExtractor : ITextExtractor
{
    public bool CanExtract(string extension) => extension is ".xlsx" or ".xlsm";

    public Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(filePath, maxCharacters, cancellationToken), cancellationToken);

    private static string? ExtractText(string filePath, int maxCharacters, CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(filePath, isEditable: false);

        WorkbookPart? workbookPart = document.WorkbookPart;

        if (workbookPart is null)
        {
            return null;
        }

        // Excel does not keep text in the cell. Repeated strings are pooled in one shared table and the
        // cell holds an index into it, so the table has to be read before any cell means anything.
        SharedStringItem[] sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            ?.Elements<SharedStringItem>()
            .ToArray() ?? [];

        var sample = new StringBuilder(maxCharacters);

        foreach (WorksheetPart worksheetPart in workbookPart.WorksheetParts)
        {
            if (ReadCells(worksheetPart, sharedStrings, sample, maxCharacters, cancellationToken))
            {
                break;
            }
        }

        return TextSample.Truncate(sample.ToString(), maxCharacters);
    }

    /// <summary>Reads one sheet's cells into the sample. Returns true once the sample is full.</summary>
    private static bool ReadCells(
        WorksheetPart worksheetPart,
        IReadOnlyList<SharedStringItem> sharedStrings,
        StringBuilder sample,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using OpenXmlReader reader = OpenXmlReader.Create(worksheetPart);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.ElementType != typeof(Cell))
            {
                continue;
            }

            var cell = (Cell)reader.LoadCurrentElement()!;

            if (TextSample.AppendUntilFull(sample, ResolveCellValue(cell, sharedStrings), maxCharacters))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveCellValue(Cell cell, IReadOnlyList<SharedStringItem> sharedStrings)
    {
        string? rawValue = cell.CellValue?.InnerText;

        if (string.IsNullOrEmpty(rawValue))
        {
            return null;
        }

        if (cell.DataType?.Value != CellValues.SharedString)
        {
            return rawValue;
        }

        bool isKnownString = int.TryParse(rawValue, out int sharedStringIndex)
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count;

        return isKnownString ? sharedStrings[sharedStringIndex].InnerText : null;
    }
}
