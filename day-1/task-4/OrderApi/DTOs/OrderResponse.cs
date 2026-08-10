namespace OrderApi.DTOs;

public sealed record OrderResponse(
    int Id,
    string CustomerName,
    string CustomerEmail,
    string ProductCode,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalAmount,
    string Status,
    DateTimeOffset CreatedAtUtc);
