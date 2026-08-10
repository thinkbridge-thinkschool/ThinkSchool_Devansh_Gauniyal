using OrderProcessing.Models;

namespace OrderProcessing.Strategies;

public interface IOrderRule
{
    string? Validate(CreateOrderRequest request);
}
