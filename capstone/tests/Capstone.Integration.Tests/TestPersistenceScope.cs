using Capstone.Invoicing.Infrastructure.Persistence;
using Capstone.Procurement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Capstone.Integration.Tests;

// Mirrors Capstone.Web's DI wiring closely enough to exercise the real
// persistence layer without booting the whole ASP.NET host: one physical
// SqlConnection behind both DbContexts, and a commit helper equivalent to
// Capstone.Web.Persistence.SharedTransactionUnitOfWork (deliberately not
// referenced directly - this test project exercises the persistence layer, not
// the host, and has no reference to Capstone.Web).
public sealed class TestPersistenceScope : IAsyncDisposable
{
    public SqlConnection Connection { get; }
    public InvoicingDbContext Invoicing { get; }
    public ProcurementDbContext Procurement { get; }

    public TestPersistenceScope(string connectionString)
    {
        Connection = new SqlConnection(connectionString);
        Invoicing = new InvoicingDbContext(
            new DbContextOptionsBuilder<InvoicingDbContext>().UseSqlServer(Connection, contextOwnsConnection: false).Options);
        Procurement = new ProcurementDbContext(
            new DbContextOptionsBuilder<ProcurementDbContext>().UseSqlServer(Connection, contextOwnsConnection: false).Options);
    }

    // Same shape as SharedTransactionUnitOfWork.SaveChangesAsync - see that
    // type's comment for why one shared connection/transaction, not two, is
    // what makes this atomic across both modules.
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await Invoicing.Database.BeginTransactionAsync(cancellationToken);
        Procurement.Database.UseTransaction(transaction.GetDbTransaction());

        var changes = await Invoicing.SaveChangesAsync(cancellationToken);
        changes += await Procurement.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    public async ValueTask DisposeAsync()
    {
        await Invoicing.DisposeAsync();
        await Procurement.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
