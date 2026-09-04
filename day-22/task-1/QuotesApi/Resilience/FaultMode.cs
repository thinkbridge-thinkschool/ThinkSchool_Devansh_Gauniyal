namespace QuotesApi.Resilience;

// TEST AND DEMO SCAFFOLDING ONLY - not production code. A real dependency doesn't
// have a mode switch; this exists purely so the breaker lifecycle (closed -> open ->
// half-open -> closed) and the other strategies can be driven precisely, on demand,
// from a test or a browser button, instead of by actually taking Redis down or
// fighting a real flaky network. See README.md "why the fault-injection switch
// exists".
public enum FaultMode
{
    Healthy,
    Failing,
    Slow
}
