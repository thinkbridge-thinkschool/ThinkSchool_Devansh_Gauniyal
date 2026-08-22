# Day 12 Task 1 — Read models + CQRS-lite

## What this is

One feature — submitting a quote, and showing a "quote wall" of quotes — split into a
write model and a read model, with no shared types between them and no event sourcing.

- **Write path**: `Features/Quotes/Commands` — `SubmitQuoteCommand` in, `SubmitQuoteResult`
  out. Validates, then saves a normalized `Quote` row via a tracked `DbContext`.
- **Read path**: `Features/Quotes/Queries` — `QuoteWallQuery` in, a `List<QuoteWallItem>`
  out. Projects straight from the database into a flat, denormalized row shaped for one
  screen, using `AsNoTracking()` and a `Select` — no `Quote` or `Author` entity is ever
  materialized.

Both paths sit on top of one SQLite database and one set of tables (`Authors`, `Quotes`).

## What CQRS-lite deliberately omits

This is CQRS in the sense of separate command and query paths through the same data — not
full CQRS with a separate read store, and not event sourcing. Specifically, this project does
**not** have:

- An event store, or any persisted domain events.
- A separate read database, or any projection-sync process copying data from a write store
  to a read store.
- Any mechanism that rebuilds state by replaying anything.

One database, one set of tables, two code paths. That is the entire scope of "CQRS-lite" as
used here.

## Why the write model is normalized and the read model isn't

The write side (`Author`, `Quote` in `Domain/`) is normalized because it has to support
validation that spans both tables — checking an author exists, checking a duplicate quote
for that author — and because normalized data is what a relational write actually needs:
one fact, one place, one constraint to enforce.

The read side (`QuoteWallItem`) is denormalized because the screen that consumes it doesn't
care that `Author` and `Quote` are separate tables — it wants one flat row with the quote
text, the author's name and country already folded on, and a pre-formatted date. Folding
`Author.Name` and `Author.Country` onto the `Quote` row is exactly what makes this a *read
model* rather than a DTO copy of the `Quote` entity: it exposes fields the write model never
puts on `Quote` directly.

## Why the read model is shaped for one screen, not reused

`QuoteWallItem` has exactly the fields the quote-wall screen displays: quote text, author
name, author country, a formatted date. It does not carry `AuthorId` (the screen doesn't
need to know it), it does not carry the raw `CreatedAt` (the screen wants a formatted string,
not a value it would have to format itself), and it does not carry anything nested. A second
screen that wanted, say, just quote counts per country would need its own query and its own
read model — widening `QuoteWallItem` to serve two screens would immediately reintroduce the
"one shape has to serve every consumer" problem CQRS-lite exists to avoid.

## The MediatR decision

The Academy's "what this builds" tag names MediatR, but the exercise body only asks for a
command handler, a query with its read model, and separate command/query paths — none of
which require a mediator library. Two things made a plain, hand-rolled dispatch the right
call here instead:

1. **Licence.** MediatR moved to a commercial licence in 2025, with a free tier below a
   revenue threshold and a paid licence above it. A student project sits in the free tier,
   but adding a package with licence terms to a graded repository is a decision worth making
   deliberately rather than by default.
2. **What the exercise actually asks for.** MediatR is not required to demonstrate CQRS —
   the separation is a folder/namespace/type structure, not a dispatch mechanism. Explicit
   handler injection (`Program.cs` constructs `SubmitQuoteHandler` and `QuoteWallHandler`
   directly) makes the dispatch visible and readable rather than hidden inside a library's
   pipeline behaviour.

**Where MediatR would slot in if adopted**: `SubmitQuoteCommand` would implement
`IRequest<SubmitQuoteResult>` instead of being a bare record, `SubmitQuoteHandler` would
implement `IRequestHandler<SubmitQuoteCommand, SubmitQuoteResult>` instead of exposing a
plain `Handle` method, and `Program.cs`'s `new SubmitQuoteHandler(context).Handle(command)`
would become `await mediator.Send(command)` with the concrete handler resolved from DI by
convention. The same substitution applies to the query side. No other part of the design
would change — the command/query/handler split already matches the shape MediatR expects.

## SQLite

EF Core with the SQLite provider, matching Days 5, 10, and 11 — no container, runs natively
on Apple Silicon, and keeps the test suite runnable in CI where there is no Docker and no
network. Nothing about this exercise is database-engine-specific. The EF Core InMemory
provider was deliberately not used: the point of the read path is the real SQL it produces,
and InMemory doesn't produce SQL to inspect.

## Evidence: real captured SQL

`dotnet run -- capture-sql` seeds a fresh database, runs one successful command, four
rejected commands, and one query, and writes what actually executed to `output/`:

- `output/command-sql.log` — the author-lookup `SELECT`, the duplicate-check `SELECT`, and
  the `INSERT`, captured from a real successful submission.
- `output/query-sql.log` — the single projection `SELECT` the query path emits.
- `output/validation-outcomes.txt` — row counts and the real `SubmitQuoteResult` returned for
  the successful case and each rejected case.

`EnableSensitiveDataLogging()` is what makes those logs show real parameter values instead
of masked placeholders — it is a development-only switch (real production code should never
ship it), and every value it logs here is synthetic seed data or synthetic test input
(`"Author 007"`, `"Synthetic quote text 00042"`, and similar) — never anything real.

This connects to [Day 10 Task 1](../../day-10/task-1) and
[Day 11 Task 2](../../day-11/task-2): projecting directly into a DTO with `Select` avoids
change-tracking cost entirely and produces SQL that only names the columns actually needed,
the same lesson both of those tasks demonstrated.

## How to run everything

From `day-12/task-1/`:

```bash
# Build
dotnet build Task1.slnx

# Run the app (two endpoints: POST /quotes, GET /quotes/wall)
dotnet run --project CqrsLite

# Capture real SQL evidence to output/ (no web host)
dotnet run --project CqrsLite -- capture-sql output/cqrslite.db output

# Run the full test suite
dotnet test Task1.slnx
```
