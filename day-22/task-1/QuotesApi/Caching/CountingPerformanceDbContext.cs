using Microsoft.EntityFrameworkCore;
using QuotesApi.Performance;

namespace QuotesApi.Caching;

// A subclass rather than a change to PerformanceDbContext itself, so the carried
// Performance/ files stay byte-for-byte what they were in day-3/task-3. Layers the
// counting interceptor on top of whatever PerformanceDbContext.OnConfiguring already
// does (including its own optional SqlLogCollector wiring).
public sealed class CountingPerformanceDbContext(
    string dataSource,
    DbQueryCounter counter,
    SqlLogCollector? logCollector = null)
    : PerformanceDbContext(dataSource, logCollector)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(new CountingCommandInterceptor(counter));
    }
}
