# Day 5 Task 6 — Notes

## What a transient failure is, and why retrying one is reasonable but retrying a 400 isn't

A transient failure is one that's likely to succeed if you just try again a moment later -- a dependency that's briefly overloaded (HTTP 503), rate-limiting you (429), timing out, or a dropped connection. A 400 Bad Request means the request itself was malformed; sending the exact same malformed request again produces the exact same 400, every time. Retrying it doesn't fix anything -- it just adds latency and load for a guaranteed-identical failure. This is why `HttpRetryStrategyOptions`' default handling only targets server errors (500+), 408, 429, and connection/timeout exceptions -- not client errors like 400 or 401.

## Exponential backoff and why jitter matters

Exponential backoff means each retry waits longer than the last (this task's real observed delays: 0.77s, then 4.48s, then 3.31s for three retries in one real test run -- growing, with randomness). If a dependency goes down and every client backs off on the *exact* same schedule, they all retry at the exact same instant, recreating the same traffic spike that just caused the failure -- a synchronized herd hitting the dependency in unison, right as it's trying to recover. Jitter (`UseJitter = true`) adds randomness to each client's delay so retries spread out over time instead of arriving all at once.

## What a circuit breaker does, what 50% over 30 seconds means, and why hammering a dead service makes things worse

A circuit breaker tracks the failure rate of recent calls and, once it crosses a threshold, stops even trying -- it fails immediately without calling the dependency at all, for a cooldown period. "50% failure rate over a 30 second sampling window" means: look at all the calls in the last 30 seconds; if at least half of them failed (and at least `MinimumThroughput` calls happened at all, so a single unlucky call can't trip it), open the circuit. Without this, every client keeps retrying against an already-struggling or fully-down dependency, adding more load exactly when it can least handle it -- turning a partial outage into a total one, and delaying recovery.

## Why the order of retry, circuit breaker, and timeout matters

This task registers them in exactly the order the Academy's own code snippet shows: `AddRetry` then `AddCircuitBreaker` then `AddTimeout`. In Polly, the first strategy added is the outermost wrapper and the last is the innermost, closest to the real call. So here: **retry (outermost) wraps circuit breaker (middle) wraps timeout (innermost)**. Concretely, that means every retry attempt passes through the circuit breaker check first -- so once the circuit opens, further retries fail fast instead of still trying -- and each individual attempt gets its own timeout budget, not the whole retry sequence sharing one clock. If timeout were outermost instead, it would cap the *entire* retry sequence as one unit, and a slow first attempt could consume the whole budget before a retry even got a chance to run. If circuit breaker were outermost instead of retry, the breaker would only ever see one execution per call instead of one per attempt, changing how quickly it can detect a real failure pattern.

## What "never silently swallow failures" means, and what it looks like done wrong

It means: when every retry has been exhausted and the dependency is still failing, the caller must receive a real failure signal (an exception, or an explicit error result) -- not a response that merely looks successful. Done wrong, this looks like: catching an exception and returning an empty list or a default value instead of rethrowing; checking `response.IsSuccessStatusCode` and ignoring the `false` case; or wrapping a call in a broad try/catch that logs and moves on. In this task, `RemoteService.GetDataAsync` calls `response.EnsureSuccessStatusCode()` specifically so a non-2xx response, even after retries are exhausted, throws `HttpRequestException` rather than returning the failed response's body as if it were data. `AllRetriesExhausted_SurfacesAsFailure_NotSilentSuccess` is the test that would fail if this were ever removed.

## Why the tests use a fake handler instead of a real server

A `ScriptedHandler` (a custom `DelegatingHandler`) returns whatever response sequence a test dictates and counts its own invocations -- no Docker, no bound port, no real network call, no internet access. A test that instead spun up a real server or hit a real network address would pass on this machine (which has Docker and internet) and could fail in a CI environment with neither, for reasons that have nothing to do with whether the resilience logic actually works. The fake handler makes the test's outcome depend only on the resilience pipeline's real behavior.

## Real configuration values and real API property names used

Verified against https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience and Polly's own source (`App-vNext/Polly` on GitHub), not memory:
- Retry: `HttpRetryStrategyOptions { MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true, OnRetry }`. `OnRetry`'s arguments expose `AttemptNumber` (zero-based) and `RetryDelay` (a `TimeSpan`) -- confirmed from Polly's `OnRetryArguments.cs` source directly, since the Microsoft docs page didn't show this.
- Circuit breaker: `HttpCircuitBreakerStrategyOptions { FailureRatio = 0.5, SamplingDuration = TimeSpan.FromSeconds(30), MinimumThroughput = 10, BreakDuration = TimeSpan.FromSeconds(30), OnOpened, OnClosed }`. `MinimumThroughput` is a real, required setting alongside `FailureRatio` -- without enough calls in the window, the ratio is never even evaluated. The production standard handler's own default `MinimumThroughput` is 100, which is why the circuit breaker test uses a separate, clearly-labeled test-only pipeline with `MinimumThroughput = 4` and a 2-second sampling window instead of waiting through a real 30-second window with 100+ calls.
- Timeout: `AddTimeout(TimeSpan.FromSeconds(10))` -- a per-attempt timeout, since it's the innermost strategy here. The timeout test similarly uses a separate test-only pipeline with a 100ms timeout, for the same reason: nobody should have to wait out a real 10-second timeout just to prove the timeout strategy works at all.

Package: `Microsoft.Extensions.Http.Resilience` version 10.9.0 -- the latest published version at the time of writing, and stable (no prerelease suffix).
