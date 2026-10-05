using OrderManagement.Enums;

namespace OrderManagement.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
