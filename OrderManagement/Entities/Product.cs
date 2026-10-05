namespace OrderManagement.Entities;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
