# Day 7 Task 3 — Set operations from a spec

A fresh, self-contained SQLite database (independent of Day 5's `quotesapi.db` and of Day 7
Tasks 1 and 2's own databases) answering three business questions with `EXCEPT`,
`INTERSECT`, and `UNION`.

## Schema interpretation

The exercise text names 'classic' and 'modern' sets without defining them. Interpretation
used here (stated openly, per the task): `Tags` carries a `Category` column restricted to
`'classic'` or `'modern'`. An author is "in the classic set" if at least one of their
quotes carries a tag whose category is `'classic'`, and likewise for `'modern'`.

## What is here

- `sql/01_schema.sql`, `sql/02_seed.sql` — schema and deterministic seed data.
- `sql/10_q1_authors_with_quotes_no_tags.sql` — Q1, via `EXCEPT`.
- `sql/11_q2_authors_in_both_sets.sql` — Q2, via `INTERSECT`.
- `sql/12_q3_combined_distinct_tags.sql` — Q3, via `UNION`.
- `sql/20_operator_contrasts.sql` — teaching file: `UNION` vs `UNION ALL` row counts,
  `EXCEPT` vs `LEFT JOIN ... IS NULL`, `INTERSECT` vs a duplicating `INNER JOIN`, and how
  `EXCEPT`/`INTERSECT` treat `NULL` as matching where plain `=` does not.
- `run.sh` — rebuilds the database from scratch and captures real query output.
- `results/*.txt` — genuine captured output, one file per query, with row counts.
- `tests/` — xunit + `Microsoft.Data.Sqlite` tests that execute the real `.sql` files from disk.

## How to run the queries

```
./run.sh
```

Rebuilds `quotes.db` from `sql/01_schema.sql` + `sql/02_seed.sql` and writes fresh output for
every query into `results/`.

## How to run the tests

```
dotnet test Task3.slnx
```

Each test builds its own temp-file SQLite database from the same schema/seed scripts and reads
the shipped `.sql` query files directly — none of the SQL is duplicated in C#.
