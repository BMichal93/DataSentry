using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Core.Retention;

namespace DataSentry.Tests.Core;

[TestFixture]
public class ReportRetentionServiceTests
{
    [Test]
    public async Task PurgeExpiredReports_OnStartup_PurgesEverythingOlderThanTheRetentionWindow()
    {
        var now = new DateTimeOffset(2026, 7, 11, 9, 0, 0, TimeSpan.Zero);
        var recordingStore = new RecordingScanResultStore();
        var retentionService = new ReportRetentionService(recordingStore, new FakeTimeProvider(now));

        await retentionService.PurgeExpiredReportsAsync();

        Assert.That(recordingStore.RequestedCutoffUtc, Is.EqualTo(now.AddDays(-RetentionPolicy.ReportRetentionDays)));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingScanResultStore : IScanResultStore
    {
        public DateTimeOffset? RequestedCutoffUtc { get; private set; }

        public Task<int> PurgeReportsOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
        {
            RequestedCutoffUtc = cutoffUtc;
            return Task.FromResult(1);
        }

        public Task SaveReportAsync(ScanReport report, IAsyncEnumerable<FileScanResult> results, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CompleteReportAsync(ScanReport report, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScanReport?> GetReportAsync(Guid reportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ScanReport>> ListReportsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<FileScanResult> GetResultsAsync(Guid reportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<DuplicateCandidate> GetDuplicateCandidatesAsync(Guid reportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ApplyDuplicateVerdictsAsync(Guid reportId, IReadOnlyList<DuplicateVerdict> verdicts, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
