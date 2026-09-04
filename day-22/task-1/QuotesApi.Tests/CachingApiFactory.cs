using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Tests;

// A plain WebApplicationFactory for the Day 21 caching endpoints - none of them
// require authorization, so this doesn't need AuthApiFactory's JWT setup. Each test
// gets its own isolated performance.db (via PERFORMANCE_DB_PATH) and its own DI
// container (so its own DbQueryCounter and HybridCache instance), and can override
// "Measurement:ArtificialDbDelayMs" / "Redis:ConnectionString" to control timing or
// point at an unreachable Redis without touching the shared local container.
//
// PERFORMANCE_DB_PATH is read via Environment.GetEnvironmentVariable at Program.cs's
// top level (process-wide, not per-factory config), so this relies on xUnit's default
// behaviour of running [Fact]s within one test class sequentially - the same
// assumption AuthApiFactory's tests already make about test isolation. Every test in
// CachingTests disposes its factory (via `using`) before the next one runs.
public sealed class CachingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _performanceDbPath =
        Path.Combine(Path.GetTempPath(), $"day21-caching-tests-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;

    public CachingApiFactory(Dictionary<string, string?>? settingsOverrides = null)
    {
        _settings = settingsOverrides ?? new Dictionary<string, string?>();
        Environment.SetEnvironmentVariable("PERFORMANCE_DB_PATH", _performanceDbPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Program.cs validates InternalJwt/InternalCaller eagerly at startup
            // (ValidateOnStart / an eager GetRequiredService) regardless of whether a
            // test ever calls an auth endpoint, so a host boot needs *some* valid
            // values here even though none of the caching tests touch auth. Added
            // first so a test's own overrides (Redis/Measurement) always win.
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = "QuotesApi.Tests.Internal",
                ["InternalJwt:Audience"] = "QuotesApi.Tests.InternalClients",
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["InternalJwt:AccessTokenLifetime"] = "00:15:00",
                ["InternalCaller:UserId"] = "caching-tests-user",
                ["InternalCaller:Email"] = "caching-tests@example.test",
                ["InternalCaller:PasswordSaltBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
                ["InternalCaller:PasswordHashBase64"] =
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            });
            configuration.AddInMemoryCollection(_settings);
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
