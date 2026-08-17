# Day 7 Task 2 — Window functions

A fresh, self-contained SQLite database (independent of Day 5's `quotesapi.db` and of Day 7
Task 1's own database) demonstrating `ROW_NUMBER`/`RANK`/`DENSE_RANK`, `LEAD`, running totals
with an explicit window frame, and a per-quote report built with `LAG` and a named `WINDOW`.

## What is here

- `sql/01_schema.sql`, `sql/02_seed.sql` — schema and deterministic seed data.
- `sql/10_row_number_vs_rank.sql` — `ROW_NUMBER`/`RANK`/`DENSE_RANK` side by side on a tie.
- `sql/11_lead_next_quote.sql` — `LEAD` to look ahead to each quote's follower.
- `sql/12_running_total.sql` — running total: default `RANGE` frame vs explicit `ROWS`.
- `sql/20_author_quote_windows.sql` — **the graded query**: one row per quote, per-author
  running count, `LAG`-based previous-quote gap in days, via a single named `WINDOW`.
- `run.sh` — rebuilds the database from scratch and captures real query output.
- `results/*.txt` — genuine captured output, one file per query, plus a labelled sample.
- `tests/` — xunit + `Microsoft.Data.Sqlite` tests that execute the real `.sql` files from disk.

## How to run the queries

```
./run.sh
```

Rebuilds `quotes.db` from `sql/01_schema.sql` + `sql/02_seed.sql` and writes fresh output for
every query into `results/`.

## How to run the tests

```
dotnet test Task2.slnx
```

Each test builds its own temp-file SQLite database from the same schema/seed scripts and reads
the shipped `.sql` query files directly — none of the SQL is duplicated in C#.
