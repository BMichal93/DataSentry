using System.Threading.Tasks;
using System.Windows;
using DataSentry.Core.Retention;
using DataSentry.Data;
using DataSentry.Core.Models;
using DataSentry.Core.Scanning;
using DataSentry.Data.Persistence.Context;
using DataSentry.UI.Scheduling;
using DataSentry.UI.ViewModels;
using DataSentry.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.UI;

/// <summary>
/// The composition root: the one place in the application allowed to know which concrete class stands
/// behind which abstraction. Everything downstream of here — every view model, the scan engine, every
/// rule — is handed what it needs through its constructor and never goes looking for it.
///
/// This is also the only file in the UI that may touch <c>DataSentry.Data</c>. A view model that
/// reached for a <c>DbContext</c> would put the database in the presentation layer, and the layering
/// would be a diagram rather than a fact.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = new ServiceCollection()
            .AddDataSentryPersistence()
            .AddDataSentryFileSystem()
            .AddDataSentryScanning()
            .AddDataSentryUserInterface()
            .BuildServiceProvider();

        // Retention runs on every launch, scheduled ones included — a purge that waited for a person
        // to open a window would not be much of a purge.
        await PrepareDatabaseAsync(_services);

        string? scheduledFolderPath = HeadlessScan.FolderPathFrom(e.Args);

        if (scheduledFolderPath is not null)
        {
            // Started by the Task Scheduler, not by a person: scan, store, exit. No window ever opens;
            // the report is waiting in the history list the next time one does. There is no screen to
            // put a failure on, so the exit code carries it to the task's own history instead.
            int exitCode = await RunScheduledScanAsync(_services, scheduledFolderPath);

            Shutdown(exitCode);
            return;
        }

        // Whatever schedule already exists is read before the window opens: the clock icon shows it
        // from the first paint, and the view model cannot load itself — nothing below the composition
        // root knows when the application has started.
        await _services.GetRequiredService<MainViewModel>().LoadAsync();

        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Brings the schema up to date, then throws away every report past its retention window. That
    /// happens here, on startup, rather than behind a button somebody has to remember to press:
    /// retention that depends on someone remembering is not retention (GDPR Art. 5(1)(e)).
    /// </summary>
    private static async Task<int> RunScheduledScanAsync(IServiceProvider services, string folderPath)
    {
        try
        {
            // There is no window here to hold an edited exclusion list, so a scheduled scan always
            // skips exactly the machine defaults — the same starting point the Search tab offers.
            IReadOnlyList<string> excludedFolders = services.GetRequiredService<IReadOnlyList<string>>();

            await services.GetRequiredService<ScanEngine>().ScanAsync(new ScanScope(folderPath, excludedFolders));

            return 0;
        }
        catch (Exception)
        {
            // Nothing a single file does gets here — the engine absorbs those. This is the scan itself
            // failing, with nobody watching; a non-zero exit is all there is to say and all that is
            // needed for the Task Scheduler history to show the run red.
            return 1;
        }
    }

    private static async Task PrepareDatabaseAsync(IServiceProvider services)
    {
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await services.GetRequiredService<ReportRetentionService>().PurgeExpiredReportsAsync();
    }
}
