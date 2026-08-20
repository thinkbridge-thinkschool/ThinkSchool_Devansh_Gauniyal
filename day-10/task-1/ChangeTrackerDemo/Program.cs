using System.Text.Json;
using ChangeTrackerDemo;

string tempDir = Path.Combine(Path.GetTempPath(), "changetrackerdemo-day10");
Directory.CreateDirectory(tempDir);
string dbPath = Path.Combine(tempDir, "catalog.db");

foreach (var suffix in new[] { "", "-wal", "-shm" })
{
    var f = dbPath + suffix;
    if (File.Exists(f)) File.Delete(f);
}

using (var seedContext = new CatalogContext(dbPath))
{
    Seeder.SeedIfNeeded(seedContext);
}

int actualRowCount;
using (var countContext = new CatalogContext(dbPath))
{
    actualRowCount = countContext.Products.Count();
}
Console.WriteLine($"Seeded {actualRowCount} rows into {dbPath}");

var report = TrackingBenchmark.Run(dbPath);

Directory.CreateDirectory(TaskPaths.OutputDirectory());
string outputPath = TaskPaths.ResultsFilePath();
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, json);

Console.WriteLine($"Tracked median: {report.Tracked.MedianElapsedMilliseconds:F3} ms, " +
                   $"{report.Tracked.MedianAllocatedBytes} bytes " +
                   $"({report.Tracked.MedianAllocatedBytesPerEntity:F1} bytes/entity)");
Console.WriteLine($"AsNoTracking median: {report.NoTracking.MedianElapsedMilliseconds:F3} ms, " +
                   $"{report.NoTracking.MedianAllocatedBytes} bytes " +
                   $"({report.NoTracking.MedianAllocatedBytesPerEntity:F1} bytes/entity)");
Console.WriteLine($"Results written to {outputPath}");
