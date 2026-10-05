using Microsoft.EntityFrameworkCore;
using OrderManagement.Entities;

namespace OrderManagement.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.StockQuantity).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasCheckConstraint("CK_Products_Stock_NonNegative", "StockQuantity >= 0");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShippingAddress).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.CustomerId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product)
                .WithMany(x => x.OrderItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
        });

        modelBuilder.Entity<IdempotencyKey>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.CreatedAt);
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
