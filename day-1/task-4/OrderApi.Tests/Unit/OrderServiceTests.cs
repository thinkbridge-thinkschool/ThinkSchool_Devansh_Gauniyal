using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services;

namespace OrderApi.Tests.Unit;

public sealed class OrderServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesNormalizedDiscountedOrder()
    {
        var repository = new FakeOrderRepository();
        var service = new OrderService(repository, NullLogger<OrderService>.Instance);
        var request = ValidRequest(quantity: 10, unitPrice: 20m);

        var response = await service.CreateAsync(request, CancellationToken.None);

        Assert.Equal(42, response.Id);
        Assert.Equal("SKU-100", response.ProductCode);
        Assert.Equal("buyer@example.com", response.CustomerEmail);
        Assert.Equal(20m, response.DiscountAmount);
        Assert.Equal(180m, response.TotalAmount);
        Assert.Equal("Pending", response.Status);
        Assert.Same(repository.SavedOrder, repository.LastOrder);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidQuantity_ThrowsValidationExceptionWithoutSaving()
    {
        var repository = new FakeOrderRepository();
        var service = new OrderService(repository, NullLogger<OrderService>.Instance);
        var request = ValidRequest(quantity: 0, unitPrice: 20m);

        var exception = await Assert.ThrowsAsync<OrderValidationException>(
            () => service.CreateAsync(request, CancellationToken.None));

        Assert.Equal("Quantity must be between 1 and 100.", exception.Message);
        Assert.Null(repository.LastOrder);
    }

    [Fact]
    public async Task CreateAsync_WhenRepositoryFails_ThrowsMeaningfulPersistenceException()
    {
        var databaseException = new DbUpdateException("Simulated database outage");
        var repository = new FakeOrderRepository { Failure = databaseException };
        var service = new OrderService(repository, NullLogger<OrderService>.Instance);

        var exception = await Assert.ThrowsAsync<OrderPersistenceException>(
            () => service.CreateAsync(ValidRequest(), CancellationToken.None));

        Assert.Equal("The order could not be saved.", exception.Message);
        Assert.Same(databaseException, exception.InnerException);
        Assert.NotNull(repository.LastOrder);
    }

    private static CreateOrderRequest ValidRequest(int quantity = 2, decimal unitPrice = 25m) => new()
    {
        CustomerName = "  Ada Lovelace  ",
        CustomerEmail = "  BUYER@Example.COM ",
        ProductCode = " sku-100 ",
        Quantity = quantity,
        UnitPrice = unitPrice
    };

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Exception? Failure { get; init; }
        public Order? LastOrder { get; private set; }
        public Order? SavedOrder { get; private set; }

        public Task<Order> AddAsync(Order order, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOrder = order;

            if (Failure is not null)
            {
                return Task.FromException<Order>(Failure);
            }

            order.Id = 42;
            SavedOrder = order;
            return Task.FromResult(order);
        }
    }
}
