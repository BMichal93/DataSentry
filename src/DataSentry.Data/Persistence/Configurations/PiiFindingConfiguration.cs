using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class PiiFindingConfiguration : IEntityTypeConfiguration<PiiFindingEntity>
{
    public void Configure(EntityTypeBuilder<PiiFindingEntity> builder)
    {
        builder.ToTable("PiiFindings");
        builder.HasKey(finding => finding.Id);

        builder.Property(finding => finding.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(finding => finding.DetectorName).IsRequired().HasMaxLength(64);

        builder.HasIndex(finding => finding.FileScanResultId);
    }
}
