using BeanStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanStock.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();

        // Synchronization key: one local product per provider product, no duplicates.
        builder.HasIndex(product => product.ExternalId).IsUnique();

        builder.Property(product => product.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(product => product.Category).HasMaxLength(Product.CategoryMaxLength).IsRequired();
        builder.Property(product => product.Price).HasPrecision(18, 2);
        builder.Property(product => product.AvailabilityStatus)
            .HasMaxLength(Product.AvailabilityStatusMaxLength)
            .IsRequired();

        // Supports the paginated listing of active products ordered by ExternalId.
        builder.HasIndex(product => new { product.IsActive, product.ExternalId });
    }
}
