using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OutboxDemo.Consumer;
using OutboxDemo.Data;
using OutboxDemo.Domain;
using OutboxDemo.Publishing;
using OutboxDemo.Relay;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=outbox.db";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IIdempotentConsumer, IdempotentConsumer>();
builder.Services.AddScoped<IMessagePublisher, InProcessFakePublisher>();
builder.Services.AddHostedService<OutboxRelayBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapPost("/orders", async (CreateOrderRequest request, AppDbContext db) =>
{
    var occurredOn = DateTime.UtcNow;
    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerName = request.CustomerName,
        Amount = request.Amount,
        CreatedOn = occurredOn
    };

    var outboxMessage = new OutboxMessage
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Type = "OrderCreated",
        Payload = JsonSerializer.Serialize(new { order.Id, order.CustomerName, order.Amount }),
        OccurredOn = occurredOn
    };

    db.Orders.Add(order);
    db.OutboxMessages.Add(outboxMessage);
    await db.SaveChangesAsync();

    return Results.Created($"/outbox/{outboxMessage.Id}", new
    {
        orderId = order.Id,
        outboxMessageId = outboxMessage.Id
    });
});

app.MapPost("/relay/run", async (AppDbContext db, IMessagePublisher publisher) =>
{
    var relay = new OutboxRelayService(db, publisher, ownerId: $"api-{Environment.ProcessId}");
    var result = await relay.ProcessOnceAsync();
    return Results.Ok(result);
});

app.MapGet("/outbox", async (AppDbContext db) =>
{
    var rows = await db.OutboxMessages
        .OrderBy(m => m.OccurredOn)
        .Select(m => new
        {
            m.Id,
            m.OrderId,
            m.Type,
            m.OccurredOn,
            m.ProcessedOn,
            m.AttemptCount,
            m.Error,
            m.ClaimedBy,
            m.ClaimedUntil
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/consumer/log", async (AppDbContext db) =>
{
    var processed = await db.ProcessedMessages
        .OrderBy(p => p.ProcessedOn)
        .ToListAsync();
    return Results.Ok(processed);
});

app.Run();

record CreateOrderRequest(string CustomerName, decimal Amount);

public partial class Program
{
}
