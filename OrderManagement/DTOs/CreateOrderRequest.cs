using System.ComponentModel.DataAnnotations;

namespace OrderManagement.DTOs;

public class CreateOrderRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required, MaxLength(500)]
    public string ShippingAddress { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public class CreateOrderItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}
