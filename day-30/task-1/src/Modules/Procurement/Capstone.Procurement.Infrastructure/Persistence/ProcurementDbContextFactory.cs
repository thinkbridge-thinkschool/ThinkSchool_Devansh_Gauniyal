using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Capstone.Procurement.Infrastructure.Persistence;

// Lets `dotnet ef migrations add` build this context at design time without the
// whole ASP.NET Core host - see
// Capstone.Invoicing.Infrastructure.Persistence.InvoicingDbContextFactory's
// identical comment for why the connection string below is a placeholder, never a
// real credential, and never used at runtime.
public sealed class ProcurementDbContextFactory : IDesignTimeDbContextFactory<ProcurementDbContext>
{
    public ProcurementDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcurementDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CapstoneDesignTime;Trusted_Connection=True;");
        return new ProcurementDbContext(optionsBuilder.Options);
    }
}
