using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Resilience;

namespace QuotesApi.Tests;

// A WebApplicationFactory for the Day 22 resilience endpoints. Same required-startup-
// config and PERFORMANCE_DB_PATH isolation as Day 21's CachingApiFactory (copied,
// not shared, to keep each factory self-contained and independently readable) plus
// one thing Day 21 never needed: the "external-service" named HttpClient normally
// makes a REAL network call to this same app's own bound port, but
// WebApplicationFactory's TestServer is in-memory and never actually binds one.
// ConfigurePrimaryHttpMessageHandler redirects that one named client's transport to
// the TestServer's own in-memory handler - the resilience DelegatingHandler chain
// (AddResilienceHandler, added in Program.cs) is unaffected, since a primary handler
// is the innermost transport, not part of that chain. This is the standard,
// documented pattern for testing a self-referencing HttpClient against
// WebApplicationFactory.
public sealed class ResilienceApiFactory : WebApplicationFactory<Program>
{
    private readonly string _performanceDbPath =
        Path.Combine(Path.GetTempPath(), $"day22-resilience-tests-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;

    public ResilienceApiFactory(Dictionary<string, string?>? settingsOverrides = null)
    {
        _settings = settingsOverrides ?? new Dictionary<string, string?>();
        Environment.SetEnvironmentVariable("PERFORMANCE_DB_PATH", _performanceDbPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "QuotesApi.Tests.Internal",
                ["InternalJwt:Audience"] = "QuotesApi.Tests.InternalClients",
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["InternalJwt:AccessTokenLifetime"] = "00:15:00",
                ["InternalCaller:UserId"] = "resilience-tests-user",
                ["InternalCaller:Email"] = "resilience-tests@example.test",
                ["InternalCaller:PasswordSaltBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                ["InternalCaller:PasswordHashBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            configuration.AddInMemoryCollection(_settings);
        });

        builder.ConfigureServices(services =>
        {
            // Verification note (see README.md's verification log): this originally
            // passed the method group `Server.CreateHandler` directly. A method-group
            // conversion still evaluates the `Server` property immediately, at the
            // point ConfigurePrimaryHttpMessageHandler is CALLED - i.e. synchronously,
            // from inside ConfigureWebHost, which runs *during* this same host's
            // construction. Reading WebApplicationFactory.Server that early reenters
            // host-building and deadlocked every test that used this factory (caught
            // live: `dotnet test` hung indefinitely at 98% CPU instead of failing or
            // passing). Wrapping in a lambda defers both the property read and the
            // call until IHttpClientFactory actually creates the handler - well after
            // the host has finished starting.
            services.AddHttpClient(DependencyKeys.ExternalService)
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var path in new[] { _performanceDbPath, _performanceDbPath + "-shm", _performanceDbPath + "-wal" })
        {
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
            }
        }
    }
}
