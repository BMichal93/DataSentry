namespace DataSentry.Core.Models;

/// <summary>
/// What a detector found in a file: the type, how many, and a redacted shape of each match —
/// "48*********12" — never the value itself. A tool that leaks the data it was built to protect is a
/// breach, so nothing here can be turned back into what was actually written in the file.
/// </summary>
/// <param name="Category">The kind of personal data.</param>
/// <param name="DetectorName">The detector that produced the finding, e.g. "Iban".</param>
/// <param name="MatchCount">How many matches the detector made.</param>
/// <param name="Confidence">0.0 to 1.0. Detection is scored, never binary.</param>
/// <param name="RedactedSnippets">
/// One masked shape per match, for the detail pane and an export the user explicitly asks for.
/// Session-only: <c>DataSentry.Data</c> never writes this to the database, so it exists for exactly as
/// long as the process that produced it does.
/// </param>
public sealed record PiiFinding(
    PiiCategory Category,
    string DetectorName,
    int MatchCount,
    double Confidence,
    IReadOnlyList<string> RedactedSnippets);
