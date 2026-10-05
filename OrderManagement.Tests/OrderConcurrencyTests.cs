using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Data;
using OrderManagement.DTOs;
using OrderManagement.Enums;
using OrderManagement.Exceptions;
using OrderManagement.Services;

namespace OrderManagement.Tests;

public sealed class OrderConcurrencyTests
{

    private static readonly Guid ProductId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid OrderId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDatabase.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    private OrderService CreateService(AppDbContext db)
    {
        return new OrderService(
            db,
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<OrderService>.Instance);
    }

    [Fact]
    public async Task Concurrent_stock_deduction_never_makes_stock_negative()
    {
        // Arrange
        await using (var db = CreateDb())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            db.Products.Add(
                new OrderManagement.Entities.Product
                {
                    Id = ProductId,
                    Name = "Concurrency Product",
                    Price = 100m,
                    StockQuantity = 15
                });

            await db.SaveChangesAsync();
        }

        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            ShippingAddress = "Jakarta",
            Items =
            [
                new()
                {
                    ProductId = ProductId,
                    Quantity = 10
                }
            ]
        };

        // Act
        var tasks = Enumerable.Range(0, 2).Select(async i =>
        {
            await using var db = CreateDb();

            try
            {
                return await CreateService(db)
                    .CreateAsync(
                        request,
                        $"different-key-{i}",
                        CancellationToken.None);
            }
            catch (ConflictException)
            {
                return null;
            }
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Count(x => x is not null)
            .Should()
            .Be(1);

        await using var verify = CreateDb();

        var product = await verify.Products
            .SingleAsync(x => x.Id == ProductId);

        product.StockQuantity.Should().Be(5);
        product.StockQuantity.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Same_idempotency_key_race_creates_only_one_order()
    {
        // Arrange
        await using (var db = CreateDb())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            db.Products.Add(
                new OrderManagement.Entities.Product
                {
                    Id = ProductId,
                    Name = "Idempotency Product",
                    Price = 100m,
                    StockQuantity = 15
                });

            await db.SaveChangesAsync();
        }

        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            ShippingAddress = "Jakarta",
            Items =
            [
                new()
                {
                    ProductId = ProductId,
                    Quantity = 10
                }
            ]
        };

        // Act
        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = CreateDb();

            return await CreateService(db)
                .CreateAsync(
                    request,
                    "same-key",
                    CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results
            .Select(x => x.Id)
            .Distinct()
            .Should()
            .ContainSingle();

        await using var verify = CreateDb();

        (await verify.Orders.CountAsync())
            .Should()
            .Be(1);

        (await verify.IdempotencyKeys.CountAsync())
            .Should()
            .Be(1);

        (await verify.Products.SingleAsync(x => x.Id == ProductId))
            .StockQuantity
            .Should()
            .Be(5);
    }

    [Fact]
    public async Task Concurrent_status_updates_only_one_wins()
    {
        // Arrange
        await using (var db = CreateDb())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            db.Orders.Add(
                new OrderManagement.Entities.Order
                {
                    Id = OrderId,
                    CustomerId = Guid.NewGuid(),
                    ShippingAddress = "Jakarta",
                    Status = OrderStatus.Confirmed,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

            await db.SaveChangesAsync();
        }

        // Act
        var a = UpdateStatus(OrderStatus.Shipped);
        var b = UpdateStatus(OrderStatus.Cancelled);

        var results = await Task.WhenAll(a, b);

        // Assert
        results.Count(x => x.Success)
            .Should()
            .Be(1);

        results.Count(
                x => !x.Success &&
                     x.Error is ConflictException)
            .Should()
            .Be(1);
    }

    private async Task<(bool Success, Exception? Error)> UpdateStatus(
        OrderStatus status)
    {
        await using var db = CreateDb();

        try
        {
            await CreateService(db)
                .UpdateStatusAsync(
                    OrderId,
                    status,
                    CancellationToken.None);

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }
}