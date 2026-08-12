# Day 3 — Task 7: Real SQL Server in CI with Testcontainers

## 1. GitHub link

NOT YET VERIFIED

## 2. Required mentor notes/deliverables

The integration suite starts one disposable SQL Server 2022 Testcontainer for the xUnit collection. Each test creates a unique database, applies the real SQL Server EF Core migration through `WebApplicationFactory`, and seeds only its own data.

CI verification: NOT YET VERIFIED

## 3. Testcontainers fixture

```csharp
using Testcontainers.MsSql;

namespace Quotes.Tests.Integration;

public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MsSqlContainerCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "SQL Server container";
}
```

## 4. WebApplicationFactory SQL Server override

```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Testing");
    builder.UseSetting("Authentication:Issuer", TestIssuer);
    builder.UseSetting("Authentication:Audience", TestAudience);
    builder.UseSetting("Authentication:SigningKey", _signingKey);

    builder.ConfigureServices(services =>
    {
        services.RemoveAll<QuotesDbContext>();
        services.RemoveAll<DbContextOptions<QuotesDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<QuotesDbContext>>();
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlServer(_connectionString));

        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(Clock);
    });
}

protected override IHost CreateHost(IHostBuilder builder)
{
    var host = base.CreateHost(builder);

    using var scope = host.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    database.Database.Migrate();

    return host;
}
```

## 5. GitHub Actions workflow snippet

```yaml
jobs:
  sql-server-integration-tests:
    runs-on: ubuntu-24.04
    timeout-minutes: 20

    steps:
      - name: Check out repository
        uses: actions/checkout@v7

      - name: Confirm x86-64 Docker host
        run: |
          test "$(uname -m)" = "x86_64"
          docker info

      - name: Set up .NET SDK
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.302

      - name: Restore Task 7
        run: dotnet restore day-3/task-7/Task7.slnx

      - name: Build Task 7
        run: dotnet build day-3/task-7/Task7.slnx --no-restore

      - name: Test Task 7 against SQL Server 2022
        run: dotnet test day-3/task-7/Task7.slnx --no-build --verbosity normal
```

## 6. CI verification

NOT YET VERIFIED

Local verification before push:

```text
dotnet restore Task7.slnx
All projects are up-to-date for restore.

dotnet build Task7.slnx --no-restore
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.82

dotnet test Quotes.Tests.Integration/Quotes.Tests.Integration.csproj --no-build --list-tests
15 SQL Server integration tests discovered.
```

## 7. Local environment limitation

LOCAL SQL SERVER TESTS NOT RUN — Docker unavailable on arm64 host.

Local restore and build succeeded. The genuine SQL Server container suite must be verified on the x86-64 GitHub Actions runner.

## 8. What did you learn this session?

I learned how Testcontainers creates a disposable SQL Server environment for integration tests and supplies its connection string to WebApplicationFactory. Running the same migrations and EF Core queries against SQL Server in CI catches provider-specific behavior that SQLite can miss.

## 9. What would break this?

The tests would fail if Docker were unavailable, the SQL Server image could not start, or migrations were incompatible with SQL Server. Shared test data could also make the suite unreliable if tests reused a database instead of creating isolated databases.
