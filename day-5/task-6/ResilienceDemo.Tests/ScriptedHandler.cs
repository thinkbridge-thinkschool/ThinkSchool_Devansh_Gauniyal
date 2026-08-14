using System.Net;
using System.Net.Http;

namespace ResilienceDemo.Tests;

// The forced-failure mechanism the Academy exercise asks for: a fake DelegatingHandler
// (used here as a primary handler, standing in for the real network) that returns
// whatever scripted sequence of responses a test dictates, and counts how many times
// it was actually invoked. That invocation count is what proves retries genuinely
// happened -- not just that the final result looked right.
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

    // For the "always fails" and "always succeeds" cases, where the exact count of
    // calls isn't known ahead of time.
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
        // A queue can't literally be infinite, but any test using this only ever
        // needs at most a handful of calls (initial attempt + configured retries),
        // so a large finite repeat is indistinguishable from "always".
        for (var i = 0; i < 1000; i++)
        {
            yield return statusCode;
        }
    }
}
