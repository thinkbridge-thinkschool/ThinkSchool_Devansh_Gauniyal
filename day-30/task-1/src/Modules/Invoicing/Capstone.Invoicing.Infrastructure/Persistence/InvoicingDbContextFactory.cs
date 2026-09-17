using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Capstone.Invoicing.Infrastructure.Persistence;

// Lets `dotnet ef migrations add` build this context WITHOUT needing the whole
// ASP.NET Core host (and its real, Key-Vault-and-managed-identity-backed
// configuration) just to generate a migration - `migrations add` never actually
// opens a connection, so the connection string here is a syntactically valid
// placeholder, never a real credential. `database update` against the real,
// private-endpoint-only Azure SQL server still goes through the host's own
// managed-identity connection string (see Program.cs) - this factory is
// design-time tooling only, never used at runtime.
public sealed class InvoicingDbContextFactory : IDesignTimeDbContextFactory<InvoicingDbContext>
{
    public InvoicingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InvoicingDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CapstoneDesignTime;Trusted_Connection=True;");
        return new InvoicingDbContext(optionsBuilder.Options);
    }
}
