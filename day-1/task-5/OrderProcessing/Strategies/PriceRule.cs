using OrderProcessing.Models;

namespace OrderProcessing.Strategies;

public sealed class PriceRule : IOrderRule
{
    public string? Validate(CreateOrderRequest request) =>
        request.UnitPrice is <= 0 or > 100_000m
            ? "Unit price must be between 0.01 and 100000.00."
            : null;
}
