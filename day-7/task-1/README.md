# Day 7 Task 1 — Joins and CTEs at depth

A fresh, self-contained SQLite database (independent of Day 5's `quotesapi.db`) demonstrating
inner/left/cross joins, a non-recursive-CTE author/quote summary, and a recursive CTE over a
self-referencing influence chain.

## What is here

- `sql/01_schema.sql`, `sql/02_seed.sql` — schema and deterministic seed data.
- `sql/10_inner_join.sql`, `sql/11_left_join.sql`, `sql/12_cross_join.sql` — join demos.
- `sql/20_author_quote_summary.sql` — **the graded query**: per-author quote count + most-recent
  quote, via two non-recursive CTEs, no correlated subquery.
- `sql/21_recursive_cte_influence_chain.sql` — recursive CTE walking the influence chain, with a
  depth cap against cycles.
- `run.sh` — rebuilds the database from scratch and captures real query output.
- `results/*.txt` — genuine captured output, one file per query.
- `tests/` — xunit + `Microsoft.Data.Sqlite` tests that execute the real `.sql` files from disk.

## How to run the queries

```
./run.sh
```

Rebuilds `quotes.db` from `sql/01_schema.sql` + `sql/02_seed.sql` and writes fresh output for
every query into `results/`.

## How to run the tests

```
dotnet test Task1.slnx
```

Each test builds its own temp-file SQLite database from the same schema/seed scripts and reads
the shipped `.sql` query files directly — none of the SQL is duplicated in C#.
