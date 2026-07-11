using DataSentry.Data.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace DataSentry.Data.Persistence;

public sealed class DataSentryDbContext : DbContext
{
    public DataSentryDbContext(DbContextOptions<DataSentryDbContext> options)
        : base(options)
    {
    }

    public DbSet<ScanReportEntity> Reports => Set<ScanReportEntity>();

    public DbSet<FileScanResultEntity> Results => Set<FileScanResultEntity>();

    public DbSet<PiiFindingEntity> Findings => Set<PiiFindingEntity>();

    public DbSet<ScanErrorEntity> Errors => Set<ScanErrorEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataSentryDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
}
