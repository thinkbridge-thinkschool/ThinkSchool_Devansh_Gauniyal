using System.Collections.Concurrent;

namespace CqrsLite.Data;

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
