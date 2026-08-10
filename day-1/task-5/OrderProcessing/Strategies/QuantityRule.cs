using OrderProcessing.Models;

namespace OrderProcessing.Strategies;

public sealed class QuantityRule : IOrderRule
{
    public string? Validate(CreateOrderRequest request) =>
        request.Quantity is < 1 or > 100
            ? "Quantity must be between 1 and 100."
            : null;
}
