using System.Net;

namespace QuotesApi.Tests;

// Copied from day-5/task-6/ResilienceDemo.Tests/ScriptedHandler.cs (namespace
// adjusted only) - see README.md/PROVENANCE.md for why: it's exactly the
// forced-failure mechanism needed to test the HTTP resilience pipeline in isolation,
// already written and already correct there. A fake DelegatingHandler (used as a
// primary handler, standing in for the real network) that returns whatever scripted
// sequence of responses a test dictates, and counts how many times it was actually
// invoked - that invocation count is what proves retries genuinely happened, not
// just that the final result looked right.
public sealed class ScriptedHandler : DelegatingHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;
    private readonly TimeSpan _delay;
    private int _invocationCount;

    public ScriptedHandler(IEnumerable<HttpStatusCode> statusCodes, TimeSpan? delay = null)
    {
        _responses = new Queue<Func<HttpResponseMessage>>(
            statusCodes.Select(code => (Func<HttpResponseMessage>)(() => new HttpResponseMessage(code))));
        _delay = delay ?? TimeSpan.Zero;
    }

    public static ScriptedHandler AlwaysReturn(HttpStatusCode statusCode, TimeSpan? delay = null) =>
        new(RepeatForever(statusCode), delay);

    public int InvocationCount => _invocationCount;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);

        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancellationToken);
        }

        lock (_responses)
        {
            var factory = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return factory();
        }
    }

    private static IEnumerable<HttpStatusCode> RepeatForever(HttpStatusCode statusCode)
    {
        for (var i = 0; i < 1000; i++)
        {
            yield return statusCode;
        }
    }
}
