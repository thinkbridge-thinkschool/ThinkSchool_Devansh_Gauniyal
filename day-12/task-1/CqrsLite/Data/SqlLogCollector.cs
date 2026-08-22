using System.Collections.Concurrent;

namespace CqrsLite.Data;

// A plain collector for QuotesDbContext's .LogTo(...) delegate. Each captured string is one
// EF Core log message; with EnableSensitiveDataLogging() on, the "Executed DbCommand"
// entries carry real parameter VALUES followed by the SQL text. ConcurrentQueue because
// EF Core's logging pipeline is not guaranteed single-threaded.
public sealed class SqlLogCollector
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string message) => _entries.Enqueue(message);

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public List<string> ExecutedCommandEntries =>
        _entries.Where(e => e.Contains("Executed DbCommand")).ToList();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
