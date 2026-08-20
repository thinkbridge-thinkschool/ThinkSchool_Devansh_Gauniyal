# Day 10 Task 2 — Query translation + projections

## What query translation is

When you write a LINQ query against a `DbSet<T>`, EF Core does not run that C# code
against an in-memory collection. It walks the expression tree and **translates** it into
SQL specific to the configured provider (here, SQLite), then sends that SQL to the
database and materialises whatever rows come back. "Query translation" is that
translate-to-SQL step. It only works for operators and expressions EF Core's SQLite
provider knows how to turn into SQL - a call to an arbitrary C# method is not one of them,
which is exactly what Task 2's "client-side evaluation" section is about.

## Why `EnableSensitiveDataLogging()` is development-only

By default, EF Core masks parameter values in its logs (e.g. `@minPrice='?'`) because a
parameter can hold anything, including personal or otherwise sensitive data.
`EnableSensitiveDataLogging()` turns that masking off so real values show up in the log -
useful for exactly this kind of investigation, dangerous in production, where it would
mean real customer data lands in log files (which are usually less access-controlled and
retained longer than the database itself). It is only enabled here, per-`CatalogContext`,
when a log collector is actually observing - and everything it captured in this
submission is synthetic seed data (see `submission.md`).

## Why projecting reduces both wire size and materialisation cost

`context.Products.Where(...).ToList()` (the BEFORE query) asks SQLite for every column on
`Product`, including the large `Description` text column - more bytes over the
connection, and a full `Product` entity (tracked, with an original-values snapshot -
see Day 10 Task 1) materialised for every row, even though a caller who only needs a
summary never touches most of those columns. `.Select(p => new ProductSummaryDto {...})`
(the AFTER query) tells EF Core exactly which columns are needed, so the generated SQL's
column list shrinks to match - proven in this submission by parsing the two real SQL
statements and counting columns, not by eyeballing them. A `ProductSummaryDto` is also
not an entity, so EF Core never tracks it: no identity map entry, no snapshot, which is
the same change-tracker cost Day 10 Task 1 measured.

## What changed in EF Core 3.0 regarding client-side evaluation

Before EF Core 3.0, if a `Where(...)` predicate contained something the provider could not
translate to SQL, EF Core would silently pull the (possibly enormous) unfiltered result
set into memory and apply the filter there with LINQ-to-Objects. This was dangerous
precisely because it was silent: a query that looked selective could quietly turn into a
full table scan with no error, no warning, and a correct-looking result - a performance
cliff nobody was told about. Since EF Core 3.0, the same query throws
`InvalidOperationException` at enumeration time instead, naming the untranslatable method
and pointing at `AsEnumerable()`/`ToList()` as the explicit way to opt into client-side
evaluation. This submission captures that real exception (see `output/evidence.json`) and
also captures the explicit `.AsEnumerable()` alternative, showing exactly what the
"silent" pre-3.0 behaviour used to do to your data: pull every row into memory first.

## How to re-run everything

From `day-10/task-2/`:

```bash
dotnet test Task2.slnx
```

A single `dotnet test` run regenerates `output/evidence.json` from scratch (via a shared
xunit collection fixture that executes every query variant against a fresh temp SQLite
database) and then both demonstrates and verifies the results in the same run - there is
no separate "run the harness, then test" step for this task.
