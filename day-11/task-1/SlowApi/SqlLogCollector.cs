namespace SlowApi;

// A plain collector for QuotesDbContext's .LogTo(...) delegate. Each captured string is
// one complete EF Core log entry; the ones that matter here are the "Executed DbCommand"
// entries, which - with EnableSensitiveDataLogging() on - carry real parameter VALUES
// followed by the SQL text.
public sealed class SqlLogCollector
{
    private readonly List<string> _entries = new();

    public void Add(string entry) => _entries.Add(entry);

    public IReadOnlyList<string> Entries => _entries;

    public IReadOnlyList<string> ExecutedCommandEntries =>
        _entries.Where(e => e.Contains("Executed DbCommand", StringComparison.Ordinal)).ToList();

    public int ExecutedCommandCount => ExecutedCommandEntries.Count;
}
