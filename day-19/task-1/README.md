# Day 19 Task 1 — Azure Service Bus topics + DLQ

## Evidence source: local emulator, not real Azure

**Everything in this repo — the code, the tests, and every file under `output/` — was produced against the local Azure Service Bus emulator running in Docker, not a real Azure Service Bus namespace.** No Azure resource was created for this task. See the Decision Log below for why, and never read the `output/` evidence as coming from real Azure.

The code itself is broker-agnostic: point `SERVICEBUS_CONNECTION_STRING` at a real Standard-tier namespace instead of the emulator and the same Publisher/Consumer binaries work unchanged.

## Scope resolutions (recorded so a mentor can challenge them)

- **Event Grid**: the task brief and exercise text never mention Event Grid — only the page's topic label ("Service Bus + Event Grid") does. Nothing was built for Event Grid: no Event Grid topic, subscription, or SDK package. This paragraph is the only place it's mentioned.
- **"Two subscriptions" vs "competing-consumer worker"**: these are two different, both-required patterns, and both are demonstrated separately (see below): `audit-sub` and `processing-sub` are two independent subscriptions on one topic (fan-out — each gets its own copy of every message); the competing-consumer demo runs multiple worker instances against the single `processing-sub` subscription (each message goes to exactly one worker).
- **Idempotency**: dedupe is implemented at the application level (`ServiceBusDemo.Core.IdempotencyStore`, tracking processed message ids in SQLite), not by turning on Service Bus's own `RequiresDuplicateDetection` feature. Both are explained and contrasted below.

## Topics vs. queues

A **queue** is point-to-point: one message, one eventual consumer (or one competing-consumer group). A **topic** is publish/subscribe: one message, delivered independently to every **subscription** on that topic. Subscriptions are the thing consumers actually receive from — each subscription is really its own hidden queue that the topic fans a copy of every message into. This demo uses one topic, `orders-topic`, with two subscriptions.

## Fan-out subscriptions vs. competing consumers

These solve different problems and are easy to conflate:

- **Fan-out (multiple subscriptions on one topic)**: used when *different, independent consumers* each need to see *every* message for their own purposes — e.g. an `audit-sub` that logs every order alongside a `processing-sub` that actually fulfills it. Every subscription gets its own full copy of the stream.
- **Competing consumers (multiple receivers on one subscription)**: used to *scale out* one workload — e.g. three worker instances all pulling from `processing-sub` so throughput scales with instance count. Service Bus's message lock guarantees a given message is only ever handed to one of those receivers at a time; if the app crashes without completing it, the lock expires and another instance gets a shot at it — which is exactly the case idempotency has to cover.

This repo demonstrates both, at the same time, off the same topic: `audit-sub` proves fan-out, `processing-sub` (with three concurrent worker instances in the `Consumer competing` command) proves competing consumers.

## Application-level dedupe vs. Service Bus's built-in duplicate detection

Service Bus has a **built-in** feature, `RequiresDuplicateDetection`, that has the broker itself reject a second message with the same `MessageId` within a configurable time window (`DuplicateDetectionHistoryTimeWindow`) — it never even reaches a consumer. It's convenient but bounded: the window is time-limited (typically minutes to a day), it only compares `MessageId`, and it does nothing for redeliveries of a message the broker already accepted (e.g. your own consumer crashed mid-processing and the lock expired).

This demo instead implements **application-level dedupe** (`IdempotencyStore`): every consumer records processed message ids in a SQLite table with `MessageId` as the primary key, and `INSERT OR IGNORE` makes "have I already handled this?" atomic even under concurrent competing-consumer instances. This is what actually needs to exist in production regardless of whether duplicate detection is turned on, because it also covers the redelivery case (message delivered, lock expires before completion, broker redelivers it) that duplicate detection does not. The emulator config (`emulator/config.json`) leaves `RequiresDuplicateDetection: false` deliberately, so every proof of dedupe in this repo's evidence comes from the application-level store, not the broker feature.

## How dead-lettering is triggered — and which one this repo demonstrates

There are three distinct triggers for a message ending up in a subscription's dead-letter sub-queue:

1. **Max delivery count exceeded** — a subscription has `MaxDeliveryCount`; once a message has been delivered and abandoned/failed that many times, the broker itself moves it to `$DeadLetterQueue` with `DeadLetterReason = "MaxDeliveryCountExceeded"`. **This is the one this repo demonstrates.** `processing-sub` is configured with `MaxDeliveryCount: 3`.
2. **Explicit dead-lettering** — application code calls `receiver.DeadLetterMessageAsync(message, reason, description)` itself, e.g. after validating a message and deciding it's malformed. Not used here, but the `Consumer` project's `poison-handle` command could be trivially changed to call this instead of abandoning, as an alternative design.
3. **TTL expiration** — if `DeadLetteringOnMessageExpiration` is `true` on the subscription, a message that sits past its `DefaultMessageTimeToLive` without being consumed is dead-lettered instead of just vanishing. Both subscriptions in this demo set this to `false`, so an expired message is simply dropped, not dead-lettered — a deliberate choice to keep the one poison-message flow unambiguous.

The trade-off: (1) is the right trigger for "my handler keeps throwing on this message" — the most realistic poison-message scenario, and the one this repo builds end to end. (2) is right when your own validation logic can immediately recognize a message is bad, without wasting delivery attempts. (3) is a safety net for messages nobody ever gets around to consuming, not a poison-message mechanism.

## Architecture

```
day-19/task-1/
  ServiceBusDemo.slnx
  emulator/
    config.json            # topic "orders-topic", subscriptions "audit-sub" (MaxDeliveryCount 10) and "processing-sub" (MaxDeliveryCount 3)
    docker-compose.yml      # Service Bus emulator + its required SQL Server Linux sidecar
  src/
    Core/                  # IdempotencyStore, DeliveryTracker — no Service Bus dependency, fully unit-testable
    Publisher/             # sends: batch | duplicate | poison
    Consumer/              # sends: fanout | competing | poison-handle | dlq-read
  tests/
    ServiceBusDemo.Tests/  # offline unit tests + gated live-broker integration tests
  output/                  # real evidence captured from running the demo against the emulator
```

## Running it yourself

```bash
cd day-19/task-1/emulator
cp ../.env.example .env   # fill in a local MSSQL_SA_PASSWORD and set ACCEPT_EULA=Y
docker compose up -d
# wait ~30-60s for the SQL Server sidecar to become healthy

cd ..
export SERVICEBUS_CONNECTION_STRING="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"

dotnet run --project src/Publisher -- batch 5
dotnet run --project src/Consumer -- fanout
dotnet run --project src/Consumer -- competing 3 15
dotnet run --project src/Publisher -- duplicate <one-of-the-published-message-ids>
dotnet run --project src/Consumer -- competing 3 10
dotnet run --project src/Publisher -- poison
dotnet run --project src/Consumer -- poison-handle 30
dotnet run --project src/Consumer -- dlq-read
```

Evidence from each step lands in `output/`.

## Secrets

No tracked file contains a real credential. `SERVICEBUS_CONNECTION_STRING` is read from an environment variable everywhere; `.env` (holding the local SQL Server password and, optionally, the connection string) is gitignored, and only `.env.example` with placeholders is tracked. The emulator's own connection string uses a fixed, publicly documented dummy `SharedAccessKey` (`SAS_KEY_VALUE`) and `UseDevelopmentEmulator=true` — even so, it is handled exactly like a real Azure connection string (env var only, never hardcoded), since a real Azure Service Bus connection string does contain a genuine `SharedAccessKey` secret.

## Verification log

- **Build**: `dotnet build ServiceBusDemo.slnx` — 0 warnings, 0 errors, all four projects (`ServiceBusDemo.Core`, `Publisher`, `Consumer`, `ServiceBusDemo.Tests`) compile.
- **Offline unit tests**: `dotnet test --filter "FullyQualifiedName!~ServiceBusIntegrationTests"` — 10/10 passed (`IdempotencyStoreTests`, `DeliveryTrackerTests`, `CompetingConsumersSimulationTests`). These require no live broker.
- **A real bug caught and fixed before any live run**: `IdempotencyStore` originally shared one `SqliteConnection` instance across the competing-consumer worker's concurrent tasks with no synchronization. Microsoft.Data.Sqlite does not document a single connection object as safe for concurrent use from multiple threads — the offline `CompetingConsumersSimulationTests` test happened to pass anyway (SQLite's own internal serialization likely absorbed it), but that's an implementation detail, not a guarantee. Caught during a design review pass before the live demo, not from an observed failure. Fix: added an internal `lock` around every `TryMarkProcessed`/`GetProcessingInstance` call, plus a `PRAGMA busy_timeout` for cross-process file-lock contention. Tests re-run green after the fix (still 10/10).
- **Required mutation check** (see full commands/output in this session's history): disabled the dedupe check in `IdempotencyStore.TryMarkProcessed` by making it unconditionally `return true`. Re-ran the offline suite: **3 tests failed for real** —
  `TryMarkProcessed_SameMessageIdTwice_OnlyFirstCallReturnsTrue`, `TryMarkProcessed_SameMessageIdFromDifferentInstance_StillOnlyProcessedOnce`, and `ConcurrentInstances_RacingOnSameMessageIds_EachMessageProcessedExactlyOnce` (expected 1 winner per message id, got 8 — every one of the 8 simulated concurrent instances "won"). Reverted the change; suite passed 10/10 again. This proves the dedupe check is actually load-bearing, not a test that would pass regardless.
- **Live emulator run — not completed.** Pulling the Service Bus emulator's container images (`mcr.microsoft.com/azure-messaging/servicebus-emulator` and its required `mcr.microsoft.com/mssql/server` sidecar) stalled repeatedly on this network. Diagnosed directly rather than assumed: DNS resolution and TLS handshake to `mcr.microsoft.com` both succeeded instantly; a small registry API call (manifest fetch) completed in under a second; but the actual blob/layer download — which Microsoft's registry redirects to a separate CDN host (`*.data.mcr.microsoft.com`, same edge IP range) — crawled at roughly 15 KB/s and then stalled completely partway through, on two independent pull attempts and against both edge IPs returned by DNS. No VPN process was running and no proxy was configured (verified via `scutil --proxy`, `~/.docker/config.json`, and Docker Desktop's settings store) — this points to ISP-level throttling of large binary transfers to Microsoft's CDN from this network, not a fixable local misconfiguration. Consequence: `output/fanout-evidence.json`, `output/processing-evidence.json`, `output/poison-handling-log.json`, and `output/dlq-evidence.json` do not exist in this submission. The "Running it yourself" section above is the exact, unchanged reproduction path for whenever the pull succeeds.
