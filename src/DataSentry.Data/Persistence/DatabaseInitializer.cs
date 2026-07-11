using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DataSentry.Data.Persistence;

/// <summary>Brings the database up to the current schema. Runs on startup, before anything reads it.</summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<DataSentryDbContext> _contextFactory;

    public DatabaseInitializer(IDbContextFactory<DataSentryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using DataSentryDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
