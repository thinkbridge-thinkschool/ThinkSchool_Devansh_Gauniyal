using OutboxDemo.Data;
using OutboxDemo.Publishing;

namespace OutboxDemo.Relay;

/// <summary>
/// Continuous polling wrapper around OutboxRelayService for real deployment.
/// Tests and the /relay/run endpoint call OutboxRelayService.ProcessOnceAsync
/// directly instead, so none of the crash/ordering/concurrency proofs depend
/// on this timer.
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval;

    public OutboxRelayBackgroundService(IServiceScopeFactory scopeFactory, TimeSpan? pollInterval = null)
    {
        _scopeFactory = scopeFactory;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();
                var relay = new OutboxRelayService(db, publisher, ownerId: "background-relay");
                await relay.ProcessOnceAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
