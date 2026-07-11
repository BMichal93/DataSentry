using DataSentry.Data.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class FileScanResultConfiguration : IEntityTypeConfiguration<FileScanResultEntity>
{
    public void Configure(EntityTypeBuilder<FileScanResultEntity> builder)
    {
        builder.ToTable("FileScanResults");
        builder.HasKey(result => result.Id);

        builder.Property(result => result.FilePath).IsRequired();
        builder.Property(result => result.Reason).IsRequired();

        // Stored as text: a database someone opens by hand should read as "Delete", not as "0".
        builder.Property(result => result.Recommendation).HasConversion<string>().HasMaxLength(16);
        builder.Property(result => result.RiskLevel).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(result => result.ReportId);

        builder
            .HasMany(result => result.Findings)
            .WithOne()
            .HasForeignKey(finding => finding.FileScanResultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
