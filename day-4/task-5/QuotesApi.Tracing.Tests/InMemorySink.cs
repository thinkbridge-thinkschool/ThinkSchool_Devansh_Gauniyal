using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace QuotesApi.Tracing.Tests;

/// <summary>
/// Captures every Serilog LogEvent emitted by the host, via the same DI test seam
/// Task 4 added to Program.cs, so this project doesn't need to reference Task 4's
/// own test project just to reuse one small class.
/// </summary>
public sealed class InMemorySink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
