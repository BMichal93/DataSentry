namespace DataSentry.Core.Models;

/// <summary>
/// What a detector found in a file: the type and how many, never the matched value itself.
/// A tool that leaks the data it was built to protect is a breach, so the value has no home
/// in this model — not in memory, not in a log, not in the database.
/// </summary>
/// <param name="Category">The kind of personal data.</param>
/// <param name="DetectorName">The detector that produced the finding, e.g. "Iban".</param>
/// <param name="MatchCount">How many matches the detector made.</param>
/// <param name="Confidence">0.0 to 1.0. Detection is scored, never binary.</param>
public sealed record PiiFinding(
    PiiCategory Category,
    string DetectorName,
    int MatchCount,
    double Confidence);
