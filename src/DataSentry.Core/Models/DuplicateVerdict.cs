namespace DataSentry.Core.Models;

/// <summary>
/// The sweep's ruling on a file it has proved to be a copy of another, to be written over the verdict
/// the classification rules gave it when the file first went past.
/// </summary>
/// <param name="Reason">Plain language: "Identical copy of C:\work\report.docx".</param>
public sealed record DuplicateVerdict(
    string FilePath,
    Recommendation Recommendation,
    string Reason);
