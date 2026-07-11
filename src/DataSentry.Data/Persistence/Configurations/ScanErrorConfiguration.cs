using DataSentry.Data.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataSentry.Data.Persistence.Configurations;

internal sealed class ScanErrorConfiguration : IEntityTypeConfiguration<ScanErrorEntity>
{
    public void Configure(EntityTypeBuilder<ScanErrorEntity> builder)
    {
        builder.ToTable("ScanErrors");
        builder.HasKey(error => error.Id);

        builder.Property(error => error.Path).IsRequired();
        builder.Property(error => error.Reason).IsRequired();

        builder.HasIndex(error => error.ReportId);
    }
}
