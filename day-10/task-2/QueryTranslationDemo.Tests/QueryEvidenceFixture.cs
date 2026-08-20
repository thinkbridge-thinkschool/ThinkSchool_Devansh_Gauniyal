using System.Text.Json;
using QueryTranslationDemo;
using Xunit;

namespace QueryTranslationDemo.Tests;

// Runs every query variant exactly once (each against its own fresh CatalogContext and
// its own SqlLogCollector, so no variant's log is polluted by another's) and writes the
// real captured evidence to output/evidence.json. Shared once across the whole test
// collection via ICollectionFixture, so this only runs a single time per test run.
public sealed class QueryEvidenceFixture : IDisposable
{
    private const decimal MinPrice = 250m;

    public string DbPath { get; }
    public EvidenceReport Report { get; }

    public QueryEvidenceFixture()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"querytranslationdemo-{Guid.NewGuid():N}.db");

        using (var seedContext = new CatalogContext(DbPath))
        {
            Seeder.SeedIfNeeded(seedContext);
        }

        Report = new EvidenceReport(
            GeneratedAtUtc: DateTime.UtcNow.ToString("O"),
            EfCoreVersion: EnvironmentInfo.EfCoreVersion(),
            DotNetVersion: EnvironmentInfo.DotNetVersion(),
            RuntimeIdentifier: EnvironmentInfo.RuntimeIdentifier(),
            Architecture: EnvironmentInfo.Architecture(),
            Before: CaptureBefore(),
            After: CaptureAfter(),
            Broken: CaptureBroken(),
            FixedQuery: CaptureFixed(),
            AsEnumerableVariant: CaptureAsEnumerable());

        Directory.CreateDirectory(TaskPaths.OutputDirectory());
        File.WriteAllText(
            TaskPaths.EvidenceFilePath(),
            JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private QueryEvidence CaptureBefore()
    {
        var collector = new SqlLogCollector();
        using var context = new CatalogContext(DbPath, collector);
        var rows = Queries.ReadProductsAboveMinPrice_WholeEntities(context, MinPrice);
        return new QueryEvidence(collector.CapturedSql() ?? string.Empty, rows.Count);
    }

    private QueryEvidence CaptureAfter()
    {
        var collector = new SqlLogCollector();
        using var context = new CatalogContext(DbPath, collector);
        var rows = Queries.ReadProductsAboveMinPrice_Projected(context, MinPrice);
        return new QueryEvidence(collector.CapturedSql() ?? string.Empty, rows.Count);
    }

    private BrokenQueryEvidence CaptureBroken()
    {
        var collector = new SqlLogCollector();
        using var context = new CatalogContext(DbPath, collector);
        try
        {
            _ = Queries.ReadProducts_BrokenUntranslatablePredicate(context);
            return new BrokenQueryEvidence("NoExceptionThrown", "Expected InvalidOperationException but none was thrown.");
        }
        catch (InvalidOperationException ex)
        {
            return new BrokenQueryEvidence(ex.GetType().FullName ?? ex.GetType().Name, ex.Message);
        }
    }

    private QueryEvidence CaptureFixed()
    {
        var collector = new SqlLogCollector();
        using var context = new CatalogContext(DbPath, collector);
        var rows = Queries.ReadProducts_FixedTranslatablePredicate(context);
        return new QueryEvidence(collector.CapturedSql() ?? string.Empty, rows.Count);
    }

    private AsEnumerableEvidence CaptureAsEnumerable()
    {
        var collector = new SqlLogCollector();
        using var context = new CatalogContext(DbPath, collector);

        int totalProductCount;
        using (var countContext = new CatalogContext(DbPath))
        {
            totalProductCount = countContext.Products.Count();
        }

        var rows = Queries.ReadProducts_AsEnumerableClientSideBoundary(context);
        return new AsEnumerableEvidence(collector.CapturedSql() ?? string.Empty, totalProductCount, rows.Count);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = DbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}

[CollectionDefinition("QueryEvidence")]
public class QueryEvidenceCollection : ICollectionFixture<QueryEvidenceFixture>
{
}
