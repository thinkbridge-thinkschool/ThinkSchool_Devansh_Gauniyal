namespace QueryTranslationDemo;

// A plain collector for CatalogContext's .LogTo(...) delegate. Each captured string is
// one complete EF Core log entry, which can itself span multiple lines - e.g. the
// "Executed DbCommand" entry has the parameters on its first line and the SQL text below.
public sealed class SqlLogCollector
{
    private readonly List<string> _entries = new();

    public void Add(string entry) => _entries.Add(entry);

    public IReadOnlyList<string> Entries => _entries;

    // Returns the full "Executed DbCommand" entry, starting from that marker rather
    // than from the SQL text itself - with EnableSensitiveDataLogging() on, this entry's
    // first line is a "Parameters=[...]" annotation carrying real parameter VALUES
    // (not masked), followed by the SQL text. Keeping that prefix is the point: it is
    // the actual evidence that sensitive data logging is doing what it's documented to do.
    public string? CapturedSql()
    {
        foreach (var entry in _entries)
        {
            int index = entry.IndexOf("Executed DbCommand", StringComparison.Ordinal);
            if (index >= 0)
            {
                return entry[index..].Trim();
            }
        }

        return null;
    }
}
