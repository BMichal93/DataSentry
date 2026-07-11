using System.Collections.Generic;
using System.Linq;
using DataSentry.Core.Models;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// Numbers, turned into the words a person would use for them. "3.1 GB", not 3_328_599_654;
/// "3 IBANs, 12 email addresses", not a list of findings.
/// </summary>
/// <remarks>
/// Every string the user reads about their files is built here, and that is deliberate: the rule that
/// the matched value of a PII finding is never shown is easier to keep when there is one place capable
/// of breaking it. <see cref="Findings"/> is handed the findings and can only reach the type and the
/// count, because <see cref="PiiFinding"/> does not carry the value at all.
/// </remarks>
internal static class PlainLanguage
{
    /// <summary>"3.1 GB", "4.1 KB", "0 bytes".</summary>
    public static string Size(long sizeBytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];

        double size = sizeBytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{sizeBytes} bytes" : $"{size:0.#} {units[unit]}";
    }

    /// <summary>"1 file", "482 files".</summary>
    public static string Files(int count) => Count(count, "file");

    /// <summary>
    /// "3 IBANs, 12 email addresses". The type and the count, never the value — the same rule that binds
    /// the log and the database binds the screen.
    /// </summary>
    public static string Findings(IReadOnlyList<PiiFinding> findings) =>
        string.Join(", ", findings.Select(finding => Count(finding.MatchCount, finding.DetectorName)));

    /// <summary>"1 IBAN", "3 IBANs", "12 email addresses".</summary>
    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count:N0} {Plural(noun)}";

    private static string Plural(string noun) =>
        noun.EndsWith("s") || noun.EndsWith("x") || noun.EndsWith("ch") || noun.EndsWith("sh")
            ? $"{noun}es"
            : $"{noun}s";
}
