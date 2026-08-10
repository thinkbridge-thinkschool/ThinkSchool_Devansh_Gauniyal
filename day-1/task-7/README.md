# Day 1 Task 7 — Collection aggregate

`Collection` is the aggregate root. It owns the collection name and its items, and it enforces the rules for adding or removing quotes.

`CollectionItem` is a value object because it describes a quote's membership using `QuoteId` and `AddedAt`. It has no independent lifecycle and its properties cannot be changed after creation.

Changes go through `Collection.AddItem` and `Collection.RemoveItem` so duplicate quotes and the 50-item limit cannot be bypassed. Controllers and repositories never add items directly to the database.

EF Core maps `CollectionItem` with `OwnsMany`, so items are stored in a separate `CollectionItems` table but remain part of the `Collection` aggregate.

## Run

```bash
cd day-1/task-7
dotnet build Task7.sln
dotnet test Task7.sln
dotnet run --project CollectionApi
```

See [curl-example.md](curl-example.md) for a duplicate-item request and its `400 ProblemDetails` response.
