using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace Capstone.Web.Api.Tests;

// Day 31 - "integration tests via WebApplicationFactory, exercising real endpoints
// through the real layers". One real SQL Server container (Testcontainers, same
// image and pattern as tests/Capstone.Integration.Tests/SqlServerFixture.cs) backs
// a real WebApplicationFactory<Program> - real routing, real minimal-API endpoint
// filters, real model binding/validation, real rate limiter, real EF Core against a
// real database engine, exercised over real HTTP, not by calling a use case
// directly. What's deliberately NOT real here: Service Bus (ServiceBus:
// FullyQualifiedNamespace is left unset, so Program.cs's serviceBusConfigured
// branch registers NoOpSupplierNotifier and never starts OutboxRelayBackgroundService
// - a CI run has no Azure Service Bus namespace to reach and no credential to reach
// it with) and Entra ID auth (Auth:TenantId/Auth:ApiAppId also left unset, matching
// the same local-dev trade-off Program.cs's own comment already names - see
// submission-day-31-task-1.md for which tests run in CI versus locally as a result).
public sealed class CapstoneApiFixture : IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        // Day 31: the rate limiter (Day 27, made configurable today - see
        // Program.cs's comment) defaults to 100 requests/minute/IP. Every test in
        // this collection shares one WebApplicationFactory instance (and so one
        // in-process "client IP"), so that limit needs raising here or an
        // ordinary xUnit test run - not even a deliberate load test - would start
        // seeing 429s partway through. This does not touch the deployed
        // environment's actual limit, which is set independently by
        // infra/modules/api.bicep (nothing there references this setting).
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CapstoneDb"] = _container.GetConnectionString(),
                    ["RateLimit:PermitsPerWindow"] = "100000",
                    // Day 26's UseAzureMonitor() throws at startup with no
                    // connection string at all - the same dummy Application
                    // Insights connection string this session has used for every
                    // local `dotnet run` since Day 29 (see the curl-walkthrough
                    // commands in submission-day-29/30-task-1.md), needed here for
                    // the identical reason: this suite runs with no real Azure
                    // Monitor resource behind it, in CI or locally.
                    ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus2-0.in.applicationinsights.azure.com/",
                });
            });
        });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CapstoneApiCollection : ICollectionFixture<CapstoneApiFixture>
{
    public const string Name = "CapstoneApi";
}
