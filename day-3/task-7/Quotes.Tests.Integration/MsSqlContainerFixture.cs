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
