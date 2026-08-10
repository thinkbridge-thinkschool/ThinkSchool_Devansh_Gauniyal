using OrderProcessing.Exceptions;
using OrderProcessing.Models;
using OrderProcessing.Strategies;

namespace OrderProcessing.Services;

public sealed class OrderService(IEnumerable<IOrderRule> rules)
{
    private const int BulkDiscountThreshold = 10;
    private const decimal BulkDiscountRate = 0.10m;

    public ProcessedOrder Process(CreateOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var rule in rules)
        {
            var error = rule.Validate(request);
            if (error is not null)
            {
                throw new OrderValidationException(error);
            }
        }

        var subtotal = request.Quantity * request.UnitPrice;
        var discount = request.Quantity >= BulkDiscountThreshold
            ? decimal.Round(subtotal * BulkDiscountRate, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ProcessedOrder(
            request.CustomerName!.Trim(),
            request.CustomerEmail!.Trim().ToLowerInvariant(),
            request.ProductCode!.Trim().ToUpperInvariant(),
            request.Quantity,
            request.UnitPrice,
            discount,
            decimal.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero));
    }
}
