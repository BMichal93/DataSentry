using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class ScanReportConfiguration : IEntityTypeConfiguration<ScanReportEntity>
{
    public void Configure(EntityTypeBuilder<ScanReportEntity> report)
    {
        report.ToTable("ScanReports");
        report.HasKey(r => r.Id);

        report.Property(r => r.RootPath).IsRequired();

        // Retention purges by scan date, so it is the one column that is always filtered on.
        report.HasIndex(r => r.CompletedUtc);

        report
            .HasMany(r => r.Results)
            .WithOne()
            .HasForeignKey(result => result.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        report
            .HasMany(r => r.Errors)
            .WithOne()
            .HasForeignKey(error => error.ReportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
