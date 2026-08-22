using System.Collections.Concurrent;

namespace DapperComparison;

public sealed class SqlLogCollector
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string message) => _entries.Enqueue(message);

    public List<string> ExecutedCommandEntries =>
        _entries.Where(e => e.Contains("Executed DbCommand")).ToList();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
