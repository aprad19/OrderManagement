namespace OrderManagement.Entities;

public class IdempotencyKey
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
}
