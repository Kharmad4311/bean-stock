using BeanStock.Domain.Catalog.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanStock.Infrastructure.Persistence.Configurations;

internal sealed class CatalogSyncRunConfiguration : IEntityTypeConfiguration<CatalogSyncRun>
{
    // Wide enough for every value of the enums, stored as text to stay readable in the database.
    private const int EnumMaxLength = 20;

    public void Configure(EntityTypeBuilder<CatalogSyncRun> builder)
    {
        builder.ToTable("CatalogSyncRuns");

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();

        builder.Property(run => run.Trigger).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(run => run.FailureReason).HasMaxLength(CatalogSyncRun.FailureReasonMaxLength);

        // Supports the "last successful synchronization" lookup made at every check of the schedule.
        builder.HasIndex(run => new { run.Status, run.CompletedAt });
    }
}
