# Day 10 Task 2 — Query translation + projections

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-10/task-2/day-10/task-2

(Points at the `day-10/task-2` folder on the `day-10/task-2` branch — not `main`.)

## Notes for mentor

### The original query and the SQL EF generated (BEFORE)

`Queries.ReadProductsAboveMinPrice_WholeEntities`, verbatim from `output/evidence.json`
(a real logged `Executed DbCommand` entry, `EnableSensitiveDataLogging()` on):

```csharp
public static List<Product> ReadProductsAboveMinPrice_WholeEntities(CatalogContext context, decimal minPrice)
{
    return context.Products
        .Where(p => p.Price > minPrice)
        .OrderBy(p => p.Id)
        .ToList();
}
```

Generated SQL, verbatim including the logged parameter value — **6 columns**:

```
Executed DbCommand (1ms) [Parameters=[@minPrice='250'], CommandType='Text', CommandTimeout='30']
SELECT "p"."Id", "p"."CategoryId", "p"."CreatedDate", "p"."Description", "p"."Name", "p"."Price"
FROM "Products" AS "p"
WHERE ef_compare("p"."Price", @minPrice) > 0
ORDER BY "p"."Id"
```

(`ef_compare` is EF Core's own SQLite function for correct decimal comparisons — SQLite
has no native decimal type — not something added by this task. `@minPrice='250'` appearing
unmasked in the `Parameters=[...]` line — rather than `@minPrice='?'` — is the real,
observed effect of `EnableSensitiveDataLogging()`.)

### The projected query and its leaner SQL (AFTER)

```csharp
public static List<ProductSummaryDto> ReadProductsAboveMinPrice_Projected(CatalogContext context, decimal minPrice)
{
    return context.Products
        .Where(p => p.Price > minPrice)
        .OrderBy(p => p.Id)
        .Select(p => new ProductSummaryDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            CategoryName = p.Category.Name
        })
        .ToList();
}
```

Generated SQL, verbatim — **4 columns** (fewer than BEFORE's 6), and the large
`Description` column is named explicitly here as the one omitted:

```
Executed DbCommand (0ms) [Parameters=[@minPrice='250'], CommandType='Text', CommandTimeout='30']
SELECT "p"."Id", "p"."Name", "p"."Price", "c"."Name" AS "CategoryName"
FROM "Products" AS "p"
INNER JOIN "Categories" AS "c" ON "p"."CategoryId" = "c"."Id"
WHERE ef_compare("p"."Price", @minPrice) > 0
ORDER BY "p"."Id"
```

Both queries returned **146 rows** — same filter, same ordering, same source, so the
comparison is like-for-like. The BEFORE/AFTER methods differ only in the terminal
`.Select(...)` (verified by a source-parsing test, `BeforeAndAfterQueryMethods_DifferOnlyInProjection`,
which strips the `.Select(...)` call from AFTER and diffs the remainder against BEFORE).

### The client-side evaluation caught and fixed

**Offending query** — calls a plain C# static helper EF Core has no SQL mapping for:

```csharp
private static bool IsPremiumName(string name) => name.ToUpperInvariant().Contains("PREMIUM");

public static List<Product> ReadProducts_BrokenUntranslatablePredicate(CatalogContext context)
{
    return context.Products
        .Where(p => IsPremiumName(p.Name))
        .ToList();
}
```

Real captured `InvalidOperationException`, verbatim from `output/evidence.json`:

```
The LINQ expression 'DbSet<Product>()
    .Where(p => Queries.IsPremiumName(p.Name))' could not be translated. Additional information: Translation of method 'QueryTranslationDemo.Queries.IsPremiumName' failed. If this method can be mapped to your custom function, see https://go.microsoft.com/fwlink/?linkid=2132413 for more information. Either rewrite the query in a form that can be translated, or switch to client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'. See https://go.microsoft.com/fwlink/?linkid=2101038 for more information.
```

**Fixed query** — same intent, expressed with operators EF Core can translate:

```csharp
public static List<Product> ReadProducts_FixedTranslatablePredicate(CatalogContext context)
{
    return context.Products
        .Where(p => p.Name.ToUpper().Contains("PREMIUM"))
        .ToList();
}
```

Generated SQL, verbatim — contains a real `WHERE` clause, proving the filter now runs in
the database, not in memory (30 rows returned; `'PREMIUM'` is embedded as a literal here
because the C# code passes a compile-time constant, not a variable, so EF Core has no
parameter to log — `Parameters=[]` is empty, not masked):

```
Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
SELECT "p"."Id", "p"."CategoryId", "p"."CreatedDate", "p"."Description", "p"."Name", "p"."Price"
FROM "Products" AS "p"
WHERE instr(upper("p"."Name"), 'PREMIUM') > 0
```

**Explicit client-side boundary** — the same untranslatable helper, made legal by
`AsEnumerable()` before the `Where`:

```csharp
public static List<Product> ReadProducts_AsEnumerableClientSideBoundary(CatalogContext context)
{
    return context.Products
        .AsEnumerable()
        .Where(p => IsPremiumName(p.Name))
        .ToList();
}
```

Its logged SQL has no `WHERE` at all:

```
Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
SELECT "p"."Id", "p"."CategoryId", "p"."CreatedDate", "p"."Description", "p"."Name", "p"."Price"
FROM "Products" AS "p"
```

— because `AsEnumerable()` runs before `Where`, all **300** seeded rows were pulled into
memory first, then filtered client-side down to **30** (matching the FIXED query's row
count, confirming both express the same logic). The translatable rewrite is preferable
because `AsEnumerable` is a full read with in-process filtering — it pulls every candidate
row across the wire regardless of how selective the filter turns out to be, where the
translatable version only ever transfers the 30 rows that actually match.

### EF Core version

**10.0.11** (confirmed via `output/evidence.json`, read from the loaded assembly's
informational version at runtime). This throw-on-untranslatable behaviour dates from
**EF Core 3.0** — before that, the same query would have silently degraded to an
in-memory filter with no error and no warning, which is precisely why it was dangerous:
a query that looked selective could quietly become a full table scan.

### Resolved interpretations (for challenge)

1. **Database** — EF Core with SQLite, natively on arm64, no container, matching Day 5
   and Day 10 Task 1. The EF Core InMemory provider was rejected because it never
   generates SQL at all, so it cannot demonstrate query translation — the entire point of
   this task.
2. **Schema** — `Product` (int id, name, category FK, decimal price, created date, and a
   deliberately large `Description` column, ~800 synthetic characters per row) plus a
   related `Category` entity so the projected query can pull a joined column
   (`CategoryName`). 300 deterministic rows across 6 categories (fixed `Random(42)` seed);
   every 10th product's name carries "Premium Edition" so the client-side-evaluation
   queries have a real, non-empty result set.
3. **SQL logging setup** — `CatalogContext` takes an optional `SqlLogCollector` and, only
   when one is supplied, calls `.LogTo(collector.Add, LogLevel.Information)` plus
   `.EnableSensitiveDataLogging()`. All captured SQL above is the real logged output from
   an actual execution, not `ToQueryString()` — `EnableSensitiveDataLogging()` is a
   development-only switch, since it writes real parameter values into logs — confirmed
   working above, where `Parameters=[@minPrice='250']` appears unmasked rather than
   `@minPrice='?'` — and **every parameter value logged in this submission is synthetic
   seed data** (a price threshold, product names, and dates generated deterministically —
   no real names, emails, or account numbers).
4. **The projection comparison** — verified structurally, not by eyeballing: a test
   parses both SQL statements' column lists and asserts AFTER has fewer columns than
   BEFORE, and a separate test asserts BEFORE's SQL contains `"Description"` while AFTER's
   does not. The AFTER query's `ProductSummaryDto` is projected, not tracked — connecting
   back to Day 10 Task 1, a DTO isn't an entity, so projecting also avoids
   change-tracker cost (no identity-map entry, no original-values snapshot).
5. **The client-side evaluation trigger** — a private static `IsPremiumName(string)`
   helper called from inside `Where(...)`. Verified by actually running it: it threw
   `InvalidOperationException` as shown above on the first attempt: no other trigger had
   to be tried.
6. **Evidence** — every SQL statement, the exception message, and every row count above
   came from `output/evidence.json`, written by a real `dotnet test` run (a shared xunit
   collection fixture executes each variant against its own fresh `CatalogContext` and
   its own log collector, then writes the file); nothing here was retyped from memory.

## What did you learn this session?

The InvalidOperationException message for the untranslatable predicate was more useful than I expected — it names the exact method that failed to translate and suggests AsEnumerable directly, instead of just saying "query failed."

## What would break this?

Adding one more property to ProductSummaryDto and mapping it from a Product column silently widens the projected SQL again, and the fewer-columns test would need to still verify that specific column stays out. Leaving EnableSensitiveDataLogging on in a production build would write real customer data into logs instead of the synthetic values captured here.
