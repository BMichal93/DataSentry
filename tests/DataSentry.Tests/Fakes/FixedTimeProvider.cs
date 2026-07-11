namespace DataSentry.Tests.Fakes;

/// <summary>A clock that does not move, so "not opened in three years" means the same thing on every run.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => nowUtc;
}
