using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace QuotesApi.Logging.Tests;

/// <summary>
/// Captures every Serilog LogEvent emitted by the host, so tests can assert on real
/// logging behaviour (e.g. correlation) without parsing console text or touching
/// process-wide Console.Out (which would be unsafe under parallel test execution).
/// </summary>
public sealed class InMemorySink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
