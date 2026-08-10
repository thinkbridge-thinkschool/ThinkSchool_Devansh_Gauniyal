using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OrderApi.DTOs;

namespace OrderApi.Tests.Integration;

public sealed class OrdersEndpointTests : IClassFixture<OrdersApiFactory>
{
    private readonly HttpClient _client;

    public OrdersEndpointTests(OrdersApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task PostOrder_ReturnsTypedCreatedResponseWithBoundaryDiscount()
    {
        // The original fails this contract: quantity 10 misses its bulk discount,
        // and success is wrapped in an anonymous { success, data } response.
        var request = new CreateOrderRequest
        {
            CustomerName = "Grace Hopper",
            CustomerEmail = "GRACE@EXAMPLE.COM",
            ProductCode = "compiler-01",
            Quantity = 10,
            UnitPrice = 20m
        };

        using var response = await _client.PostAsJsonAsync("/api/orders", request);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(order);
        Assert.True(order.Id > 0);
        Assert.Equal("COMPILER-01", order.ProductCode);
        Assert.Equal("grace@example.com", order.CustomerEmail);
        Assert.Equal(20m, order.DiscountAmount);
        Assert.Equal(180m, order.TotalAmount);
        Assert.Equal($"/api/orders/{order.Id}", response.Headers.Location!.OriginalString);
    }
}

public sealed class OrdersApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"order-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDatabase"] = $"Data Source={_databasePath}"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
