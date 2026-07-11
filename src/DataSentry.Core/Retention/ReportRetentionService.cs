using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;

namespace DataSentry.Core.Retention;

/// <summary>
/// Purges expired reports. Called on startup, never from a button — retention that depends on
/// someone remembering to press something is not retention.
/// </summary>
public sealed class ReportRetentionService
{
    private readonly IScanResultStore _resultStore;
    private readonly TimeProvider _timeProvider;

    public ReportRetentionService(IScanResultStore resultStore, TimeProvider timeProvider)
    {
        _resultStore = resultStore;
        _timeProvider = timeProvider;
    }

    /// <summary>Deletes reports older than the retention window, and returns how many went.</summary>
    public Task<int> PurgeExpiredReportsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoffUtc = RetentionPolicy.CutoffFrom(_timeProvider.GetUtcNow());
        return _resultStore.PurgeReportsOlderThanAsync(cutoffUtc, cancellationToken);
    }
}
