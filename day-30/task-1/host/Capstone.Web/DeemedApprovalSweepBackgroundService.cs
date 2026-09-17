using Capstone.Invoicing.Application.UseCases;
using Capstone.Web.Persistence;

namespace Capstone.Web;

// Day 30 - the scheduling half of DESIGN.md's deemed-approval SLA sweep.
// ApplyDeemedApprovalsUseCase itself is deliberately a plain callable use case,
// not a background service (see that type's own comment on why a scheduled
// sweep is a different mechanism from an event queue) - this class is the
// "something" its comment always said would need to call it. Runs on its own
// PeriodicTimer, in a fresh DI scope per tick (the app-wide singleton services
// this depends on are fine to reuse, but ApplyDeemedApprovalsUseCase and its
// IInvoiceRepository/IUnitOfWork are request-scoped, so a genuinely new scope is
// required, not the scope this background service itself started in).
//
// Verifying this fires is a real constraint worth naming plainly: this uses
// TimeProvider.System, the real deployed clock, because a fake one here would
// only prove the WIRING runs, not that the actual timer/use-case combination
// works against real data - a demo clock would misrepresent the real cadence
// entirely. That means live-firing a 10-day review window cannot be
// demonstrated within one working session. What CAN be demonstrated live: the
// sweep runs against real Azure SQL on a real schedule and correctly finds
// nothing to act on for a fresh invoice (see
// submission-day-30-task-1.md's curl walkthrough) - firing itself is proven by
// tests/Capstone.Integration.Tests, which controls its own clock the same way
// the domain unit tests already do.
public sealed class DeemedApprovalSweepBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DeemedApprovalSweepBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = double.TryParse(configuration["DeemedApprovalSweep:IntervalMinutes"], out var configured)
            ? configured
            : 15; // A placeholder cadence, not a researched one - see DESIGN.md's own "a real system would need this window contractually agreed" note on the review window itself; the SWEEP frequency has the same "someone needs to actually decide this" status.

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        do
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep tick must not crash the host - there will be
                // another tick. Logged, not swallowed silently.
                logger.LogError(ex, "Deemed-approval sweep tick failed; will retry on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var applyDeemedApprovals = scope.ServiceProvider.GetRequiredService<ApplyDeemedApprovalsUseCase>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var deemed = await applyDeemedApprovals.ExecuteAsync(cancellationToken);
        if (deemed.Count == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Deemed-approval sweep approved {Count} invoice(s): {InvoiceIds}",
            deemed.Count,
            string.Join(", ", deemed));
    }
}
