using DataSentry.Core.Abstractions;
using DataSentry.Core.Retention;
using DataSentry.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.Data;

public static class DataSentryDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite persistence layer. <paramref name="databasePath"/> is for tests, which
    /// point it at a temporary file; the app leaves it null and gets the default location.
    /// </summary>
    public static IServiceCollection AddDataSentryPersistence(
        this IServiceCollection services,
        string? databasePath = null)
    {
        string resolvedDatabasePath = databasePath ?? DatabaseLocation.GetDefaultDatabasePath();

        // A factory rather than a scoped context: the desktop app is long-lived and has no request
        // boundary to scope a DbContext to.
        services.AddDbContextFactory<DataSentryDbContext>(options =>
            options.UseSqlite(DatabaseLocation.ToConnectionString(resolvedDatabasePath)));

        services.AddSingleton<IScanResultStore, SqliteScanResultStore>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<ReportRetentionService>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
