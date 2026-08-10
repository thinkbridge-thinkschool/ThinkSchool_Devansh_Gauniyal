namespace OrderProcessing.Models;

public sealed class CreateOrderRequest
{
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? ProductCode { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
