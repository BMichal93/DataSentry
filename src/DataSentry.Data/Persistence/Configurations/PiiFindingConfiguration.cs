using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class PiiFindingConfiguration : IEntityTypeConfiguration<PiiFindingEntity>
{
    public void Configure(EntityTypeBuilder<PiiFindingEntity> finding)
    {
        finding.ToTable("PiiFindings");
        finding.HasKey(f => f.Id);

        finding.Property(f => f.Category).HasConversion<string>().HasMaxLength(32);
        finding.Property(f => f.DetectorName).IsRequired().HasMaxLength(64);

        finding.HasIndex(f => f.FileScanResultId);
    }
}
