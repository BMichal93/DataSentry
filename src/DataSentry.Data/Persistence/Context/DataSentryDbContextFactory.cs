using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataSentry.Data.Persistence.Context;

/// <summary>Exists so `dotnet ef migrations add` can build a context without starting the app.</summary>
public sealed class DataSentryDbContextFactory : IDesignTimeDbContextFactory<DataSentryDbContext>
{
    public DataSentryDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<DataSentryDbContext> options = new DbContextOptionsBuilder<DataSentryDbContext>()
            .UseSqlite(DatabaseLocation.ToConnectionString(DatabaseLocation.GetDefaultDatabasePath()))
            .Options;

        return new DataSentryDbContext(options);
    }
}
