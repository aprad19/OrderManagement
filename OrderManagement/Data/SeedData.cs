using Microsoft.EntityFrameworkCore;
using OrderManagement.Entities;

namespace OrderManagement.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        db.Products.AddRange(
            new Product { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Product A", Price = 100000m, StockQuantity = 100 },
            new Product { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Product B", Price = 250000m, StockQuantity = 15 },
            new Product { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "Product C", Price = 50000m, StockQuantity = 50 });

        await db.SaveChangesAsync();
    }
}
