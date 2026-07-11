using DataSentry.Core.Models;

namespace DataSentry.Core.Classification;

/// <summary>
/// When a file counts as forgotten. Shared by the staleness rule and by the recommendation logic,
/// which needs the same answer for a different question: ordinary personal data in a stale file is
/// worth a human's time, in a file still in use it is not.
/// </summary>
public static class StalenessPolicy
{
    /// <summary>
    /// Two years. Long enough that last year's tax return is still "in use", short enough that the
    /// export somebody made for a meeting in 2021 is not.
    /// </summary>
    public const int StaleAfterDays = 730;

    private const double DaysPerYear = 365.25;

    public static bool IsStale(FileMetadata file, DateTimeOffset nowUtc) =>
        (nowUtc - LastTouched(file)).TotalDays >= StaleAfterDays;

    /// <summary>How long the file has been left alone, as the user would say it: "Not opened in 3 years".</summary>
    public static string Describe(FileMetadata file, DateTimeOffset nowUtc)
    {
        int years = (int)((nowUtc - LastTouched(file)).TotalDays / DaysPerYear);

        return years == 1
            ? "Not opened in a year"
            : $"Not opened in {years} years";
    }

    /// <summary>
    /// The later of the two timestamps. Reading a file updates the access time but not the write time,
    /// and Windows leaves access-time tracking off on many volumes — so neither timestamp can be
    /// trusted alone, and a file is only forgotten when both say so.
    /// </summary>
    private static DateTimeOffset LastTouched(FileMetadata file) =>
        file.LastModifiedUtc > file.LastAccessedUtc ? file.LastModifiedUtc : file.LastAccessedUtc;
}
