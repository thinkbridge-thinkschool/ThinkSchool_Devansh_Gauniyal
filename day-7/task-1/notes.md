# Day 7 Task 1 — Notes

## Why a CTE over a correlated subquery (the one-line answer the exercise asks for)

A CTE computes each author's most-recent quote exactly once, via a single `ROW_NUMBER()` pass
over `Quotes`, then joins that result back to `Authors`; a correlated subquery in the `SELECT`
list would instead re-execute a `Quotes` scan once per author row, and still couldn't return the
quote's text *and* its timestamp *and* stay tie-broken by `Id` without correlating twice over.

## Database engine decision

Day 5's Quotes DB (`day-5/task-2/QuotesApi`) is SQLite via `Microsoft.EntityFrameworkCore.Sqlite`
(connection string `Data Source=quotesapi.db`) — confirmed before writing anything. Per the task's
decision gate, this task builds a **fresh, self-contained** SQLite database from its own
`sql/01_schema.sql` + `sql/02_seed.sql`, and never opens, reads, or modifies Day 5's `.db` file or
source. Its entities are actually called `Author`/`Book` (not `Quote`) — this task's schema uses
the `Authors`/`Quotes`/`Tags`/`QuoteTags` shape given in the task text instead.

## Schema

- `Authors (Id, Name, InfluencedByAuthorId)` — nullable self-reference for the influence chain.
- `Quotes (Id, AuthorId, Text, CreatedAt)` — `CreatedAt` stored as ISO-8601 text
  (`YYYY-MM-DDTHH:MM:SS`) so ordering the plain TEXT column is chronologically correct.
- `Tags (Id, Name)`, `QuoteTags (QuoteId, TagId)` — many-to-many.
- All FKs declared; `PRAGMA foreign_keys = ON` is required per-connection (SQLite parses but
  does not enforce FKs otherwise) — set in `run.sh` and in the test harness.

## Seed data — deliberate cases

10 authors, 25 quotes, 6 tags, all fixed literal Ids/timestamps (no `CURRENT_TIMESTAMP`, no
randomness — every run is byte-identical):
- **Zero-quote author**: Confucius (Id 7).
- **Genuine tie**: Marcus Aurelius's Quotes 9 and 10 both carry `CreatedAt = 2023-08-01T09:00:00`;
  the `ORDER BY CreatedAt DESC, Id DESC` tie-break picks Id 10 deterministically.
- **3-level influence chain**: Ryan Holiday (4) → Marcus Aurelius (3) → Epictetus (2) → Seneca (1).
- **Unused tag**: `unused-tag` (Id 6) has no `QuoteTags` rows at all.

## Recursive CTE guard

`21_recursive_cte_influence_chain.sql` caps recursion at depth 20 (`WHERE ic.Depth < 20`).
SQLite has no built-in cycle detection for a self-referencing walk like this one — without the
cap, a cycle in `InfluencedByAuthorId` (A influences B, B influences A) would make the recursive
step re-fire forever. `tests/RecursiveCteTests.cs` seeds exactly that synthetic cycle and asserts
the query still terminates.

## Tests

`tests/` uses xunit + `Microsoft.Data.Sqlite` only (no EF Core, per the tech-stack constraint).
Each test builds a fresh temp-file SQLite database from `01_schema.sql` + `02_seed.sql`, and reads
the real `.sql` query files from disk via a `CopyToOutputDirectory` link in the csproj — none of
the SQL is duplicated as C# string literals. 10/10 tests pass:
`dotnet test day-7/task-1/Task1.slnx`.

## Runner

`run.sh` rebuilds `quotes.db` from scratch and captures genuine `sqlite3` stdout into
`results/*.txt` — one file per query, full result for the small queries, `results/20_...txt`
explicitly labelled `TOP 10 ROWS` for the graded query (the seed's 10 authors mean this happens
to be the complete result too). `quotes.db` itself is gitignored, same as Day 5's `quotesapi.db`
— it's fully regenerable from the committed `.sql` scripts.
