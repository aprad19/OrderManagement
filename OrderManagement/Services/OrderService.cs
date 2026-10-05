using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Data;
using OrderManagement.DTOs;
using OrderManagement.Entities;
using OrderManagement.Enums;
using OrderManagement.Exceptions;

namespace OrderManagement.Services;

public sealed class OrderService(AppDbContext db, ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new UnprocessableEntityException("Idempotency-Key header is required.");

        if (idempotencyKey.Length > 200)
            throw new UnprocessableEntityException("Idempotency-Key must not exceed 200 characters.");

        ValidateCreateRequest(request);
        var requestHash = ComputeRequestHash(request);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Reserve the idempotency key BEFORE touching stock. This is what makes
            // the same-key race safe even when two requests arrive simultaneously.
            var reservation = new IdempotencyKey
            {
                Id = Guid.NewGuid(),
                Key = idempotencyKey,
                RequestHash = requestHash,
                CreatedAt = DateTime.UtcNow
            };

            db.IdempotencyKeys.Add(reservation);
            await db.SaveChangesAsync(cancellationToken);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress.Trim(),
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Validate product existence and aggregate duplicate product IDs first.
            var requestedItems = request.Items
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToList();

            var productIds = requestedItems.Select(x => x.ProductId).ToList();
            var products = await db.Products
                .Where(x => productIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            foreach (var item in requestedItems)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    throw new NotFoundException($"Product '{item.ProductId}' was not found.");

                var affectedRows = await db.Products
                    .Where(x => x.Id == item.ProductId && x.StockQuantity >= item.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.StockQuantity, x => x.StockQuantity - item.Quantity), cancellationToken);

                if (affectedRows != 1)
                    throw new ConflictException($"Insufficient stock for product '{product.Name}'.");

                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price
                });
            }

            db.Orders.Add(order);
            reservation.OrderId = order.Id;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Order {OrderId} created with idempotency key {IdempotencyKey}", order.Id, idempotencyKey);
            return ToResponse(order);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            // Another concurrent transaction owns the same key. It has either
            // committed an order or is about to commit one. Read it after rollback.
            var existing = await db.IdempotencyKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Key == idempotencyKey, cancellationToken);

            if (existing is null || existing.OrderId is null)
                throw new ConflictException("The idempotency key is currently being processed. Please retry.");

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new ConflictException("The same Idempotency-Key was already used with a different request payload.");

            return await GetByIdAsync(existing.OrderId.Value, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return order is null
            ? throw new NotFoundException($"Order '{id}' was not found.")
            : ToResponse(order);
    }

    public async Task<PagedResult<OrderResponse>> ListAsync(
        OrderStatus? status,
        Guid? customerId,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Orders.AsNoTracking().Include(x => x.Items).AsQueryable();

        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId.Value);
        if (fromDate.HasValue) query = query.Where(x => x.CreatedAt >= fromDate.Value.ToUniversalTime());
        if (toDate.HasValue) query = query.Where(x => x.CreatedAt < toDate.Value.ToUniversalTime().AddDays(1));

        var totalItems = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        return new PagedResult<OrderResponse>(orders.Select(ToResponse).ToList(), page, pageSize, totalItems, totalPages);
    }

    public async Task<OrderResponse> UpdateStatusAsync(Guid id, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
            throw new NotFoundException($"Order '{id}' was not found.");

        if (!IsValidTransition(order.Status, newStatus))
            throw new ConflictException($"Invalid order status transition: {order.Status} -> {newStatus}.");

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The order was modified by another request. Please reload and retry.");
        }
    }

    public async Task<OrderResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var order = await db.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
            throw new NotFoundException($"Order '{id}' was not found.");

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Confirmed))
            throw new ConflictException($"Order cannot be cancelled from status '{order.Status}'.");

        try
        {
            // Restore each item atomically. The row-version on Order protects
            // against a simultaneous status change such as Confirmed -> Shipped.
            foreach (var item in order.Items)
            {
                await db.Products
                    .Where(x => x.Id == item.ProductId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.StockQuantity, x => x.StockQuantity + item.Quantity), cancellationToken);
            }

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(order);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new ConflictException("The order was modified by another request. Please reload and retry.");
        }
    }

    private static void ValidateCreateRequest(CreateOrderRequest request)
    {
        if (request.CustomerId == Guid.Empty)
            throw new UnprocessableEntityException("CustomerId is required.");

        if (string.IsNullOrWhiteSpace(request.ShippingAddress))
            throw new UnprocessableEntityException("ShippingAddress is required.");

        if (request.Items is null || request.Items.Count == 0)
            throw new UnprocessableEntityException("At least one order item is required.");

        if (request.Items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0))
            throw new UnprocessableEntityException("Each item must contain a valid ProductId and a positive Quantity.");

        foreach (var group in request.Items.GroupBy(x => x.ProductId))
        {
            if (group.Sum(x => (long)x.Quantity) > int.MaxValue)
                throw new UnprocessableEntityException($"Total quantity for product '{group.Key}' is too large.");
        }
    }

    private static string ComputeRequestHash(CreateOrderRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.CustomerId,
            ShippingAddress = request.ShippingAddress.Trim(),
            Items = request.Items
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .OrderBy(x => x.ProductId)
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static bool IsValidTransition(OrderStatus current, OrderStatus next) => (current, next) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed) => true,
        (OrderStatus.Pending, OrderStatus.Cancelled) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
        (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
        (OrderStatus.Shipped, OrderStatus.Delivered) => true,
        _ => false
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private static OrderResponse ToResponse(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.Status,
            order.CreatedAt,
            order.UpdatedAt,
            Convert.ToBase64String(order.RowVersion),
            order.Items.Select(x => new OrderItemResponse(x.ProductId, x.Quantity, x.UnitPrice)).ToList());
}
