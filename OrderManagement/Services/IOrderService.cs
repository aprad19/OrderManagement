using OrderManagement.DTOs;
using OrderManagement.Enums;

namespace OrderManagement.Services;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<OrderResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<OrderResponse>> ListAsync(OrderStatus? status, Guid? customerId, DateTime? fromDate, DateTime? toDate, int page, int pageSize, CancellationToken cancellationToken);
    Task<OrderResponse> UpdateStatusAsync(Guid id, OrderStatus newStatus, CancellationToken cancellationToken);
    Task<OrderResponse> CancelAsync(Guid id, CancellationToken cancellationToken);
}
