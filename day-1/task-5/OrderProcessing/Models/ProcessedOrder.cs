namespace OrderProcessing.Models;

public sealed record ProcessedOrder(
    string CustomerName,
    string CustomerEmail,
    string ProductCode,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalAmount);
