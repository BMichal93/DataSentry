using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class ScanErrorConfiguration : IEntityTypeConfiguration<ScanErrorEntity>
{
    public void Configure(EntityTypeBuilder<ScanErrorEntity> error)
    {
        error.ToTable("ScanErrors");
        error.HasKey(e => e.Id);

        error.Property(e => e.Path).IsRequired();
        error.Property(e => e.Reason).IsRequired();

        error.HasIndex(e => e.ReportId);
    }
}
