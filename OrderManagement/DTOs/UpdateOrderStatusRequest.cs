using OrderManagement.Enums;

namespace OrderManagement.DTOs;

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}
