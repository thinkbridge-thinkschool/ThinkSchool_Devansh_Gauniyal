using Microsoft.Extensions.Logging;

namespace ResilienceDemo.Tests;

// One real, structured log entry, captured exactly as the resilience pipeline wrote
// it -- not reconstructed from a formatted string. State is kept as the raw
// key/value pairs (attempt number, reason, delay, etc.) so tests can assert on real
// values instead of parsing text.
public sealed record CapturedLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> State);

// A minimal in-memory ILoggerProvider so tests can inspect exactly what the
// resilience pipeline logged, with no mocking library involved.
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

            // Also write to the console so a real `dotnet test` run shows the
            // actual retry log lines directly, since the log output itself is a
            // required deliverable, not just something asserted on internally.
            Console.WriteLine($"[{logLevel}] {message}");
        }
    }
}
