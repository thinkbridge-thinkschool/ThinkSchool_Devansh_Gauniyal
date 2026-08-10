using Microsoft.EntityFrameworkCore;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Models;
using OrderApi.Repositories;

namespace OrderApi.Services;

public sealed class OrderService(
    IOrderRepository repository,
    ILogger<OrderService> logger) : IOrderService
{
    private const int BulkDiscountThreshold = 10;
    private const decimal BulkDiscountRate = 0.10m;

    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBusinessRules(request);

        var subtotal = request.Quantity * request.UnitPrice;
        var discount = request.Quantity >= BulkDiscountThreshold
            ? decimal.Round(subtotal * BulkDiscountRate, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var order = new Order
        {
            CustomerName = request.CustomerName!.Trim(),
            CustomerEmail = request.CustomerEmail!.Trim().ToLowerInvariant(),
            ProductCode = request.ProductCode!.Trim().ToUpperInvariant(),
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            DiscountAmount = discount,
            TotalAmount = decimal.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero),
            Status = "Pending",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            var savedOrder = await repository.AddAsync(order, cancellationToken);
            logger.LogInformation("Created order {OrderId} for product {ProductCode}",
                savedOrder.Id, savedOrder.ProductCode);
            return ToResponse(savedOrder);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Database failure while creating an order for {ProductCode}",
                order.ProductCode);
            throw new OrderPersistenceException("The order could not be saved.", exception);
        }
    }

    private static void ValidateBusinessRules(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            throw new OrderValidationException("Customer name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            throw new OrderValidationException("Customer email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductCode))
        {
            throw new OrderValidationException("Product code is required.");
        }

        if (request.Quantity is < 1 or > 100)
        {
            throw new OrderValidationException("Quantity must be between 1 and 100.");
        }

        if (request.UnitPrice is <= 0 or > 100_000m)
        {
            throw new OrderValidationException("Unit price must be between 0.01 and 100000.00.");
        }
    }

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.CustomerName,
        order.CustomerEmail,
        order.ProductCode,
        order.Quantity,
        order.UnitPrice,
        order.DiscountAmount,
        order.TotalAmount,
        order.Status,
        order.CreatedAtUtc);
}
