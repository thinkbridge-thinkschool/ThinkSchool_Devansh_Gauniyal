# Day 10 Task 1 — EF Core change tracker + AsNoTracking

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-10/task-1/day-10/task-1

(Points at the `day-10/task-1` folder on the `day-10/task-1` branch — not `main`.)

## Notes for mentor

### The two query variants

From `ChangeTrackerDemo/TrackingBenchmark.cs`, adjacent and identical except for the
`.AsNoTracking()` call (verified by a source-parsing test, `QueryVariantFairnessTests`):

```csharp
public static List<Product> ReadAllTracked(CatalogContext context)
{
    return context.Products
        .OrderBy(p => p.Id)
        .ToList();
}

public static List<Product> ReadAllNoTracking(CatalogContext context)
{
    return context.Products
        .AsNoTracking()
        .OrderBy(p => p.Id)
        .ToList();
}
```

### Timing and allocation difference (10,000 rows, real run, `output/results.json`)

1 discarded warmup iteration + 5 measured iterations per variant, fresh `DbContext` per
iteration, `GC.Collect()` before each measured iteration:

| Run              | Tracked (ms) | Tracked (bytes) | AsNoTracking (ms) | AsNoTracking (bytes) |
|------------------|-------------:|----------------:|------------------:|---------------------:|
| warmup (discarded) | 40.977     | 13,140,264      | 10.985            | 7,067,288             |
| 1                | 23.857       | 13,000,496      | 8.259              | 6,877,416             |
| 2                | 23.451       | 13,000,496      | 8.235              | 6,877,416             |
| 3                | 23.176       | 13,000,496      | 8.298              | 6,877,416             |
| 4                | 25.126       | 13,000,496      | 8.204              | 6,877,416             |
| 5                | 24.098       | 13,000,496      | 8.383              | 6,877,416             |
| **Median**       | **23.857**   | **13,000,496**  | **8.259**          | **6,877,416**         |

Both variants returned exactly 10,000 rows on every iteration. Per-entity allocation:
tracked = 1300.05 bytes/entity, AsNoTracking = 687.74 bytes/entity. AsNoTracking was
**65.4% faster** (23.857 ms → 8.259 ms) and allocated **47.1% less** (1300.05 →
687.74 bytes/entity) on this run. This is a single-process, un-isolated measurement
(Stopwatch + `GC.GetAllocatedBytesForCurrentThread()`, no BenchmarkDotNet) — treat the
numbers as directionally honest, not laboratory-grade.

### Identity resolution (real captured output, `IdentityResolutionTests.cs`)

- Tracked context, same key queried twice: `Assert.Same(first, second)` — **true**, same
  object reference.
- `AsNoTracking()` context, same key queried twice: `Assert.NotSame(first, second)` —
  **true**, two distinct objects.
- Tracked context: `first.Name` mutated in memory (no save) to `"Mutated-In-Memory-Only"`,
  then queried again — the second query returned that in-memory value, not the original
  database value, proving it did not re-materialise the row.
- `ChangeTracker.Entries().Count()` after a tracked `ToList()`: **greater than 0**. After
  an `AsNoTracking()` `ToList()`: **exactly 0**.

### Tracked vs. untracked `SaveChanges()` (real captured output, `TrackedVsUntrackedSaveChangesTests.cs`)

- Tracked entity's `Name` changed, `SaveChanges()` called, re-read from a **fresh**
  context: new name **is present**. Change persisted.
- `AsNoTracking()` entity's `Name` changed, `SaveChanges()` called, re-read from a fresh
  context: original name **is still there**, unchanged. The write was silently dropped —
  there was no tracked snapshot for `SaveChanges()` to diff against.

### When you would NOT use `AsNoTracking()`

Never use it when you intend to modify the entities you read and persist those changes
with `SaveChanges()` — as the demonstration above shows, there is no snapshot to diff
against, so the edit is silently lost with no exception and no warning.

### Resolved interpretations (for challenge)

1. **Schema** — one `Product` entity (int id, name, category, decimal price, description),
   10,000 deterministic synthetic rows (fixed `Random(42)` seed), safely re-seedable.
2. **Database** — a real, file-based SQLite database under the OS temp directory (never
   inside the repo), not the EF Core InMemory provider and not a shared-cache in-memory
   connection, because a real file makes "fresh `DbContext` per iteration" trivial to
   reason about — the file persists independently of any connection's lifetime.
3. **Measurement tool** — `System.Diagnostics.Stopwatch` +
   `GC.GetAllocatedBytesForCurrentThread()` (BCL only). BenchmarkDotNet was rejected as an
   unnecessary extra dependency for what the task asks; the tradeoff is single-process,
   non-statistically-rigorous numbers.
4. **Measurement hygiene** — fresh `DbContext` per iteration (the single most important
   fairness property — verified by a source test, not just assumed), 1 discarded warmup +
   5 measured iterations per variant, `GC.Collect()` + `GC.WaitForPendingFinalizers()`
   before each measured iteration so the previous iteration's garbage isn't billed to the
   current one, and the two variants asserted to return the same row count.
5. **Package versions** — `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11 (not Day 5's
   10.0.10: 10.0.10 pulls `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which NuGet flags with a
   high-severity advisory, GHSA-2m69-gcr7-jv3q/CVE-2025-6965; 10.0.11 pulls 2.1.12, which
   clears the warning on a clean `dotnet build`). `Microsoft.NET.Test.Sdk` 18.0.1, `xunit`
   2.9.3, `xunit.runner.visualstudio` 3.1.5 — matching Day 5's already-proven arm64
   combination; `xunit.runner.visualstudio` 4.0.0 exists on NuGet but targets `xunit.v3`,
   a different package line from the `xunit` 2.x used here, so it was not adopted.
6. **"When not to use it"** — grounded in the SaveChanges demonstration above, not
   asserted from memory.

Why tracking costs what it does: for every tracked row, EF Core keeps an identity-map
entry keyed by primary key plus a copy of the original column values so `SaveChanges()`
has something to diff against — that snapshot and map entry are the extra allocation, and
their cost scales linearly with rows read, which is exactly what the 10k-row measurement
shows.

## What did you learn this session?

The tracked/untracked SaveChanges test was the one that actually surprised me — I expected an error or a no-op warning, not a silent, successful-looking write that just does nothing to the database.

## What would break this?

An AsNoTracking read followed by an edit and SaveChanges loses the write with zero exception, so a developer only finds out from a support ticket. The measured win also shrinks at low row counts, where connection-open cost dominates over tracking cost.
