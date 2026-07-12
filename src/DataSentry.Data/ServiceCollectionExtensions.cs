using System.Runtime.Versioning;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Retention;
using DataSentry.Data.FileSystem;
using DataSentry.Data.Persistence.Context;
using DataSentry.Data.TextExtraction;
using DataSentry.Data.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.Data;

public static class ServiceCollectionExtensions
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

    /// <summary>
    /// Registers the real file system as the thing a scan reads, and the extractors that get text out
    /// of the formats found there. A new format is a new <see cref="ITextExtractor"/> registered here
    /// — no existing class is touched to add one.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddDataSentryFileSystem(this IServiceCollection services)
    {
        services.AddSingleton<IFileSource, FileSystemFileSource>();
        services.AddSingleton<IFileContentReader, FileContentReader>();

        // Shared by every extractor that reads words off pixels; created once because a Tesseract
        // engine is expensive, disposed by the container because it owns native memory.
        services.AddSingleton<OcrEngine>();

        services.AddSingleton<ITextExtractor, SpreadsheetTextExtractor>();
        services.AddSingleton<ITextExtractor, WordDocumentTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, ImageOcrTextExtractor>();

        // The fallback for every other extension, registered as itself: it is not one of the format
        // extractors above and must not be chosen as though it were.
        services.AddSingleton<PlainTextExtractor>();

        return services;
    }
}
