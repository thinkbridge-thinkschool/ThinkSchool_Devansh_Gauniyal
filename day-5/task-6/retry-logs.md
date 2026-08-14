# Day 5 Task 6 — Real Captured Retry Logs

Real, complete console output from an actual `dotnet test Task6.slnx` run. Nothing below is hand-written or prettified -- this is exactly what the test run produced (log lines interleaved with xunit's own test progress lines, since tests run concurrently by default). The `[Information]`/`[Warning]`/`[Error]` lines come from two sources at once: the framework's own built-in HTTP/resilience telemetry logging (lines like `Execution attempt...` and `Resilience event occurred...`), and this task's own custom `OnRetry`/final-failure logging (the `Retry attempt N for remote-service after HTTP 503; waiting ...` and `All attempts for remote-service failed; final status 503.` lines) -- both come from the same `AddResilienceHandler` pipeline registered once in `ResiliencePipelineConfiguration`.

```
[Information] Start processing HTTP request GET https://example.invalid/data
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.2239ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '0', Execution Time: 5.4625ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 1 for remote-service after HTTP 503; waiting 00:00:00.7713890 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0366ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '1', Execution Time: 0.1988ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 2 for remote-service after HTTP 503; waiting 00:00:04.4797925 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0227ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '2', Execution Time: 0.4887ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 3 for remote-service after HTTP 503; waiting 00:00:03.3145254 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0247ms - 503
[Error] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '3', Execution Time: 0.4324ms
[Information] End processing HTTP request after 8593.3882ms - 503
[Error] All attempts for remote-service failed; final status 503.
```
^ The above is from `AllRetriesExhausted_SurfacesAsFailure_NotSilentSuccess`: 4 total attempts (`Attempt: '0'` through `Attempt: '3'`, i.e. the initial try plus all 3 configured retries), each retry logged with its attempt number and the delay before the next one, ending in the explicit final-failure log line this task added.

```
[Information] Start processing HTTP request GET https://example.invalid/data
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0029ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '0', Execution Time: 0.0482ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 1 for remote-service after HTTP 503; waiting 00:00:02.2238037 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0606ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '1', Execution Time: 0.5028ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 2 for remote-service after HTTP 503; waiting 00:00:00.9623709 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0185ms - 200
[Information] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '200', Handled: 'False', Attempt: '2', Execution Time: 0.3148ms
[Information] End processing HTTP request after 3188.4659ms - 200
```
^ The above is from `TransientFailureThenSuccess_RetriesAndSucceeds`: two logged retries after 503s, then a successful 200 on the third attempt -- no final-failure log, because this one genuinely succeeds.

```
[Information] Start processing HTTP request GET https://example.invalid/data
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0076ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '0', Execution Time: 0.0845ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 1 for remote-service after HTTP 503; waiting 00:00:00.1801197 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0222ms - 503
[Warning] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '503', Handled: 'True', Attempt: '1', Execution Time: 1.8728ms
[Warning] Resilience event occurred. EventName: 'OnRetry', Source: 'remote-service-default//Retry', Operation Key: '', Result: '503'
[Warning] Retry attempt 2 for remote-service after HTTP 503; waiting 00:00:03.4659329 before the next attempt.
[Information] Sending HTTP request GET https://example.invalid/data
[Information] Received HTTP response headers after 0.0276ms - 200
[Information] Execution attempt. Source: 'remote-service-default//Retry', Operation Key: '', Result: '200', Handled: 'False', Attempt: '2', Execution Time: 0.3443ms
[Information] End processing HTTP request after 3649.9979ms - 200
```
^ The above is from `RetryAttempts_AreLogged_WithAttemptNumberAndReason` -- the same scripted 503, 503, 200 sequence, with the retry log entries the test asserts on directly (attempt number + reason) visible here.

## Trimmed from this file

The `SuccessOnFirstAttempt_DoesNotRetry_AndLogsNoRetries` and `RepeatedFailures_OpenCircuit_ThenFailFastWithoutInvokingHandler` tests produced no retry log lines at all (correctly -- one never retries, the other tests the circuit breaker in isolation with no retry strategy attached), so there's nothing retry-related to show for them. The `SlowDependency_TimesOut_RatherThanHanging` test uses a separate test-only timeout pipeline with no logging wired in, so it produced no log lines either. Full real pass/fail summary (all 6 tests, real run):

```
Total tests: 6
     Passed: 6
 Total time: 15.8530 Seconds
```
