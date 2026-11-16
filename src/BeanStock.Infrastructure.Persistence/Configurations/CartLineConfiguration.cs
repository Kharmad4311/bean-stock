using BeanStock.Domain.Carts;
using BeanStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanStock.Infrastructure.Persistence.Configurations;

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("CartLines", table => table.HasCheckConstraint(
            "CK_CartLines_Quantity",
            $"[Quantity] BETWEEN {CartRules.MinQuantity} AND {CartRules.MaxQuantity}"));

        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        // A product appears at most once in a cart (rule 5).
        builder.HasIndex(line => new { line.CartId, line.ProductId }).IsUnique();

        builder.Property(line => line.ProductName).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(line => line.UnitPrice).HasPrecision(18, 2);

        // Concurrency token: an update or delete only applies to the line version read.
        builder.Property(line => line.Version).IsConcurrencyToken();

        builder.Ignore(line => line.Subtotal);

        // Products are deactivated, never deleted: deleting a product still referenced by a cart is refused.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
