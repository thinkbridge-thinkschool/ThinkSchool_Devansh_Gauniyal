# Thinkbridge Submission Pack: Day 2, Task 3

## Academy requirement

Create an xUnit and Fluent Assertions project named `Tests.Domain` with six pure, fast tests for the `Collection` aggregate:

1. Empty name throws.
2. Name longer than 80 characters throws.
3. Adding the 51st item throws.
4. Adding a duplicate quote ID throws.
5. Removing a non-existent item throws.
6. Adding and then removing an item leaves zero items.

The tests must not use a database, `DbContext`, `WebApplicationFactory`, dependency injection, fixtures, mocks, or real system time.

## Complete CollectionTests class

```csharp
using CollectionApi.Exceptions;
using CollectionApi.Models;
using FluentAssertions;
using Xunit;

namespace Tests.Domain;

public sealed class CollectionTests
{
    private static readonly DateTimeOffset FixedAddedAt = new(
        2026, 8, 11, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreatingCollection_WithEmptyName_Throws()
    {
        Action act = () => new Collection(string.Empty, ownerId: 1);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void CreatingCollection_WithNameLongerThan80Characters_Throws()
    {
        Action act = () => new Collection(new string('a', 81), ownerId: 1);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingFiftyFirstItem_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        for (var quoteId = 1; quoteId <= Collection.MaximumItems; quoteId++)
        {
            collection.AddItem(quoteId, FixedAddedAt);
        }

        Action act = () => collection.AddItem(51, FixedAddedAt);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingDuplicateQuoteId_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        Action act = () => collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void RemovingNonExistentItem_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);

        Action act = () => collection.RemoveItem(quoteId: 42);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingThenRemovingItem_LeavesCollectionEmpty()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        collection.RemoveItem(quoteId: 42);

        collection.Items.Should().BeEmpty();
    }
}
```

## Actual unedited test output

```text
Test run for /Users/devansh/thinkschool/day-2/task-3/Tests.Domain/bin/Debug/net10.0/Tests.Domain.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 13 ms - Tests.Domain.dll (net10.0)
```

- Total: 6
- Passed: 6
- Failed: 0
- Skipped: 0
- Actual reported domain-test duration: 13 ms

The first detailed run reported 0.4607 seconds for the complete VSTest process, including discovery and test-host startup. Its xUnit timestamps showed the actual execution phase between 0.09 and 0.13 seconds. Three additional domain-only runs reported 13 ms, 18 ms, and 17 ms respectively.

## Required aggregate correction

Task 2 returned `false` when removing a missing item. The Academy specification requires a domain exception, so only the Task 3 copy was corrected:

```csharp
public void RemoveItem(int quoteId)
{
    var item = _items.SingleOrDefault(item => item.QuoteId == quoteId)
        ?? throw new CollectionInvariantException(
            $"Quote {quoteId} is not in this collection.");

    _items.Remove(item);
}
```

The Task 3 service now treats a missing collection as `false`, while a missing item remains the aggregate's invariant failure:

```csharp
public async Task<bool> RemoveQuoteAsync(
    int collectionId,
    int quoteId,
    CancellationToken cancellationToken)
{
    var collection = await repository.GetByIdAsync(collectionId, cancellationToken);
    if (collection is null)
    {
        return false;
    }

    collection.RemoveItem(quoteId);
    await repository.UpdateAsync(collection, cancellationToken);
    return true;
}
```

## Verification

- Restore succeeded.
- `Task3.slnx` build succeeded with 0 warnings and 0 errors.
- All six required tests passed.
- Three additional runs passed with 13 ms, 18 ms, and 17 ms reported durations.
- `Tests.Domain` contains no database, web factory, network, DI, fixture, setup, or mock code.
- Day 1, Task 1, and Task 2 were unchanged.
- Generated `bin` and `obj` directories are ignored.

## Git and GitHub

- Branch: `day-2/task-3`
- Implementation commit: `516ef3d2b57e846b75abdaed5ffda86fac485fdb`
- Pull-request creation URL: https://github.com/devansh-gauniyal/thinkschool/pull/new/day-2/task-3
- Final folder link: https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-3
