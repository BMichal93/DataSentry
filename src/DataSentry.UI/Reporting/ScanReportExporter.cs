using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Models;

namespace DataSentry.UI.Reporting;

/// <summary>
/// Writes a scan's results to a CSV file the user chose — the export the spec calls for, generated only
/// when asked and never written anywhere on its own. Streams the same way the store already streams a
/// whole report, so exporting one costs a constant amount of memory whatever its size.
/// </summary>
public sealed class ScanReportExporter
{
    public async Task ExportAsync(
        IAsyncEnumerable<FileScanResult> results,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(destinationPath);

        await writer.WriteLineAsync(ScanReportCsvFormatter.Header);

        await foreach (FileScanResult result in results.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(ScanReportCsvFormatter.FormatRow(result));
        }
    }
}
