using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class FileScanResultConfiguration : IEntityTypeConfiguration<FileScanResultEntity>
{
    public void Configure(EntityTypeBuilder<FileScanResultEntity> result)
    {
        result.ToTable("FileScanResults");
        result.HasKey(r => r.Id);

        result.Property(r => r.FilePath).IsRequired();
        result.Property(r => r.Reason).IsRequired();

        // Stored as text: a database someone opens by hand should read as "Delete", not as "0".
        result.Property(r => r.Recommendation).HasConversion<string>().HasMaxLength(16);
        result.Property(r => r.RiskLevel).HasConversion<string>().HasMaxLength(16);

        result.HasIndex(r => r.ReportId);

        result
            .HasMany(r => r.Findings)
            .WithOne()
            .HasForeignKey(finding => finding.FileScanResultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
