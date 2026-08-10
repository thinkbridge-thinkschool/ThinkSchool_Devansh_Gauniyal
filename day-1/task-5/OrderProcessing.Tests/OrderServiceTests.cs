using OrderProcessing.Exceptions;
using OrderProcessing.Models;
using OrderProcessing.Services;
using OrderProcessing.Strategies;

namespace OrderProcessing.Tests;

public sealed class OrderServiceTests
{
    // Test: validation rejects orders with negative quantity
    [Fact]
    public void Process_NegativeQuantity_ThrowsValidationError()
    {
        var service = CreateService();
        var request = ValidRequest(quantity: -1);

        var exception = Assert.Throws<OrderValidationException>(() => service.Process(request));

        Assert.Equal("Quantity must be between 1 and 100.", exception.Message);
    }

    // Test: a valid order is processed and gets the discount at exactly 10 items
    [Fact]
    public void Process_ValidOrder_ReturnsProcessedOrder()
    {
        var service = CreateService();
        var request = ValidRequest(quantity: 10, unitPrice: 20m);

        var result = service.Process(request);

        Assert.Equal("SKU-100", result.ProductCode);
        Assert.Equal("buyer@example.com", result.CustomerEmail);
        Assert.Equal(20m, result.DiscountAmount);
        Assert.Equal(180m, result.TotalAmount);
    }

    // Test: validation rejects an order with an empty product code
    [Fact]
    public void Process_EmptyProductCode_ThrowsValidationError()
    {
        var service = CreateService();
        var request = ValidRequest(productCode: "   ");

        var exception = Assert.Throws<OrderValidationException>(() => service.Process(request));

        Assert.Equal("Product code is required.", exception.Message);
    }

    private static OrderService CreateService() => new(
        new IOrderRule[]
        {
            new RequiredFieldsRule(),
            new QuantityRule(),
            new PriceRule()
        });

    private static CreateOrderRequest ValidRequest(
        int quantity = 2,
        decimal unitPrice = 25m,
        string productCode = " sku-100 ") => new()
    {
        CustomerName = "Ada Lovelace",
        CustomerEmail = " BUYER@Example.COM ",
        ProductCode = productCode,
        Quantity = quantity,
        UnitPrice = unitPrice
    };
}
