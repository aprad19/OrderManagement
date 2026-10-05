using OrderManagement.Enums;

namespace OrderManagement.DTOs;

public record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string ShippingAddress,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RowVersion,
    IReadOnlyCollection<OrderItemResponse> Items);

public record OrderItemResponse(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);

public record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
