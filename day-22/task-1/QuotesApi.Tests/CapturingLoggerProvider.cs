using Microsoft.Extensions.Logging;

namespace QuotesApi.Tests;

// Copied from day-5/task-6/ResilienceDemo.Tests/CapturingLogger.cs (namespace
// adjusted only) - see README.md/PROVENANCE.md. A minimal in-memory ILoggerProvider
// so tests can inspect exactly what the resilience pipeline logged (real structured
// state - attempt number, delay, etc. - not a reconstructed string), with no mocking
// library involved.
public sealed record CapturedLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> State);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLogEntry> _entries = [];

    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private void Add(CapturedLogEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);

            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();

            owner.Add(new CapturedLogEntry(logLevel, message, values));
            Console.WriteLine($"[{logLevel}] {message}");
        }
    }
}
