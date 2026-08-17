## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-7/task-1/day-7/task-1

## Notes for mentor
Fresh, self-contained SQLite database (`sql/*.sql`, built by `run.sh`) — deliberately independent of Day 5's `quotesapi.db`/EF Core schema, confirmed as SQLite before building anything. Schema: `Authors` (nullable self-referencing `InfluencedByAuthorId`), `Quotes`, `Tags`, `QuoteTags`. Seed data (10 authors, 25 quotes) deliberately includes a zero-quote author (Confucius), a genuine `CreatedAt` tie for Marcus Aurelius's two most recent quotes, a 3-level influence chain (Ryan Holiday → Marcus Aurelius → Epictetus → Seneca), and an unused tag. The graded query (`20_author_quote_summary.sql`) uses two non-recursive CTEs (`QuoteCounts`, `RankedQuotes` via `ROW_NUMBER()`) joined back to `Authors` — no correlated subquery in the `SELECT` list; `Id DESC` is the explicit deterministic tie-break. `21_recursive_cte_influence_chain.sql` uses `WITH RECURSIVE` with a depth cap against cycles. `tests/` (xunit + `Microsoft.Data.Sqlite`, no EF Core) reads and executes the real `.sql` files from disk rather than duplicating them in C#; 10/10 tests pass, including one that seeds a synthetic author cycle to prove the depth cap actually terminates instead of hanging. Full SQL + top-10 result set for the graded query is in `results/20_author_quote_summary.txt`; why a CTE over a correlated subquery is answered in `notes.md`.

## What did you learn this session?
`ROW_NUMBER() PARTITION BY` plus a deterministic `Id` tie-break replaces a correlated subquery cleanly for "pick one row per group," and SQLite needs `WITH RECURSIVE` for the whole `WITH` clause even when only one of several CTEs in it is actually recursive.

## What would break this?
A cycle in `InfluencedByAuthorId` would recurse forever without the depth cap — verified by seeding a synthetic 2-author cycle in a test and confirming the query still terminates.
