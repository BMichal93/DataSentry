using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DataSentry.Data.Persistence;

/// <summary>
/// SQLite cannot compare or sort a DateTimeOffset in SQL, which the retention purge and the report
/// list both need to do. Every timestamp DataSentry stores is UTC, so the offset carries no
/// information and the value goes to the database as a plain UTC DateTime.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeOffsetConverter()
        : base(
            timestamp => timestamp.UtcDateTime,
            storedValue => new DateTimeOffset(DateTime.SpecifyKind(storedValue, DateTimeKind.Utc)))
    {
    }
}
