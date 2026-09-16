using Capstone.Invoicing.Infrastructure.Persistence;
using Capstone.Procurement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Capstone.Web.Persistence;

// Gives Invoicing and Procurement's separate DbContexts one atomic commit,
// without a distributed transaction (Azure SQL does not support those - see
// infra/README.md and THREAT-MODEL.md for other places this project already
// works around Azure SQL/Service Bus platform limits rather than assuming a
// textbook option is available). This works because both contexts are
// registered against the exact SAME SqlConnection instance (see Program.cs) -
// two contexts sharing one physical connection can share one local
// DbTransaction on it; two contexts each opening their own connection could
// not, even to the same database, without MSDTC.
//
// Why this needs to exist at all: DESIGN.md requires PO capacity reservation to
// be synchronous with invoice submission specifically so two concurrent
// submissions can't both race past the same PO's remaining capacity. That
// invariant is only real if the Invoice insert and the PurchaseOrder update
// commit together or not at all - two separate SaveChanges calls on unrelated
// connections would let one succeed while the other failed, exactly the gap
// SubmitInvoiceUseCase's own comment names as unsolved by the in-memory
// scaffold this replaces.
public sealed class SharedTransactionUnitOfWork(
    InvoicingDbContext invoicing,
    ProcurementDbContext procurement) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await invoicing.Database.BeginTransactionAsync(cancellationToken);
        procurement.Database.UseTransaction(transaction.GetDbTransaction());

        var changes = await invoicing.SaveChangesAsync(cancellationToken);
        changes += await procurement.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }
}
