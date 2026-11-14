using BeanStock.Domain.Carts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BeanStock.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id).ValueGeneratedNever();

        builder.Property(cart => cart.UserId).HasMaxLength(CartRules.UserIdMaxLength).IsRequired();

        // At most one cart per user, even under parallel creation requests (rule 1).
        builder.HasIndex(cart => cart.UserId).IsUnique();

        builder.Ignore(cart => cart.Total);

        builder.HasMany(cart => cart.Lines)
            .WithOne()
            .HasForeignKey(line => line.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // The aggregate exposes a read-only view; EF Core reads and writes the backing field.
        builder.Navigation(cart => cart.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
