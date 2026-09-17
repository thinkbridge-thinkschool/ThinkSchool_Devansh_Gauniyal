using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Capstone.Integration.Tests;

// One SQL Server container for the whole test run (expensive to start, cheap to
// reuse - every test uses unique GUIDs for its own aggregates, so there's no
// cross-test collision risk from sharing one database). Migrations are applied
// once here, via the exact same InvoicingDbContext/ProcurementDbContext.Database
// .MigrateAsync() calls Program.cs runs on startup against the real deployed
// database - see that file's comment for why migrate-on-startup is this
// project's only option against the real (private-endpoint-only) Azure SQL, and
// why proving it here, against a real SQL Server engine, matters.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer _container = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var scope = new TestPersistenceScope(ConnectionString);
        await scope.Invoicing.Database.MigrateAsync();
        await scope.Procurement.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
