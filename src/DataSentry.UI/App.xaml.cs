using System.Threading.Tasks;
using System.Windows;
using DataSentry.Core.Retention;
using DataSentry.Data;
using DataSentry.Data.Persistence.Context;
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

        await PrepareDatabaseAsync(_services);

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
    private static async Task PrepareDatabaseAsync(IServiceProvider services)
    {
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await services.GetRequiredService<ReportRetentionService>().PurgeExpiredReportsAsync();
    }
}
