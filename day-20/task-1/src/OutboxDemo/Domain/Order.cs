namespace OutboxDemo.Domain;

public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedOn { get; set; }

    public List<OutboxMessage> OutboxMessages { get; set; } = new();
}
