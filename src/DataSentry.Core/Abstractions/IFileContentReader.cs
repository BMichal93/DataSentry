using System.Threading;
using System.Threading.Tasks;

namespace DataSentry.Core.Abstractions;

/// <summary>
/// Reaches inside a file. The two expensive operations in a scan live here, and both are deliberate:
/// a rule asks for them only once a cheaper signal has said it is worth it.
/// </summary>
/// <remarks>
/// Both methods throw on a file they cannot read (denied, locked, gone since it was enumerated).
/// The scan engine catches that once, records a <see cref="Models.ScanError"/> and moves to the next
/// file — so the reason for the failure survives, rather than being flattened into a null.
/// </remarks>
public interface IFileContentReader
{
    /// <summary>
    /// The first <paramref name="maxCharacters"/> characters of the file's text — enough for a PII
    /// detector to sample, without reading a two-gigabyte export end to end. Whether that text sat in
    /// a .csv, a spreadsheet or a PDF is the data layer's problem; Core is handed a string either way.
    /// </summary>
    /// <returns>The text, or null when the file holds no text to sample — a JPEG, a binary blob.</returns>
    Task<string?> ReadTextSampleAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A hash of the file's full content, for confirming that two files of the same size really are
    /// the same file. The whole file has to be read, so this is the last question a rule should ask.
    /// </summary>
    Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken = default);
}
