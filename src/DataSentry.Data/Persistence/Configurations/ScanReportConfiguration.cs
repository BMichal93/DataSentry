using DataSentry.Data.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class ScanReportConfiguration : IEntityTypeConfiguration<ScanReportEntity>
{
    public void Configure(EntityTypeBuilder<ScanReportEntity> builder)
    {
        builder.ToTable("ScanReports");
        builder.HasKey(report => report.Id);

        builder.Property(report => report.RootPath).IsRequired();

        // Retention purges by scan date, so it is the one column that is always filtered on.
        builder.HasIndex(report => report.CompletedUtc);

        builder
            .HasMany(report => report.Results)
            .WithOne()
            .HasForeignKey(result => result.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(report => report.Errors)
            .WithOne()
            .HasForeignKey(error => error.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
