using OrderProcessing.Models;

namespace OrderProcessing.Strategies;

public sealed class RequiredFieldsRule : IOrderRule
{
    public string? Validate(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return "Customer name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return "Customer email is required.";
        }

        return string.IsNullOrWhiteSpace(request.ProductCode)
            ? "Product code is required."
            : null;
    }
}
