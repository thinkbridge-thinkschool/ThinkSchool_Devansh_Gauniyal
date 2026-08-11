# Day 2, Task 3: Test the Domain Layer

## What the domain layer is

The domain layer holds the business concepts and rules that must remain true regardless of HTTP, databases, or user interfaces. In this project, `Collection` is an aggregate: it controls its name and its group of quote items so callers cannot put it into an invalid state.

## Aggregate invariants

An invariant is a rule the aggregate always enforces. The `Collection` aggregate requires:

- a non-empty name between 3 and 80 characters;
- no more than 50 items;
- no duplicate quote ID;
- removal only when the quote ID exists.

The Task 2 aggregate returned `false` when asked to remove a missing quote. The Academy specification requires that operation to throw, so the Task 3 copy now throws the existing `CollectionInvariantException`. Only the Task 3 copy was changed.

## Why domain tests are pure and fast

These rules operate entirely on an in-memory object. They do not need ASP.NET Core, dependency injection, EF Core setup, a database, network access, mocks, or fixtures. Avoiding those dependencies makes failures easier to understand and keeps the suite fast enough to run continuously while coding.

The tests use one fixed `DateTimeOffset`, so they never depend on the computer clock. Each test creates its own aggregate, so there is no shared mutable state or test-order dependency.

## Arrange, act, assert

- Arrange creates the aggregate and any starting state.
- Act performs the behavior being tested, often captured as a Fluent Assertions `Action`.
- Assert checks the resulting exception or state.

Fluent Assertions reads close to plain English. For example, `act.Should().Throw<CollectionInvariantException>()` states the expected rule directly, while `collection.Items.Should().BeEmpty()` clearly describes the final state.

## What the six tests prove

1. `CreatingCollection_WithEmptyName_Throws` proves an empty name is rejected.
2. `CreatingCollection_WithNameLongerThan80Characters_Throws` uses exactly 81 characters and proves the maximum length.
3. `AddingFiftyFirstItem_Throws` adds 50 unique IDs and proves item 51 is rejected.
4. `AddingDuplicateQuoteId_Throws` proves the same quote cannot be added twice.
5. `RemovingNonExistentItem_Throws` proves removal fails loudly when the item does not exist.
6. `AddingThenRemovingItem_LeavesCollectionEmpty` proves the normal add/remove state transition.

## Important files

- `CollectionApi/Models/Collection.cs`: aggregate and invariants.
- `CollectionApi/Exceptions/CollectionInvariantException.cs`: domain exception.
- `CollectionApi/Services/CollectionService.cs`: updated for the corrected removal contract.
- `CollectionApi/Controllers/CollectionsController.cs`: maps removal invariant failures consistently.
- `Tests.Domain/Tests.Domain.csproj`: xUnit and Fluent Assertions project.
- `Tests.Domain/CollectionTests.cs`: six pure invariant tests.
- `SUBMISSION.md`: complete test class and actual output.
- `MENTOR-NOTES.txt`: ready-to-paste Academy evidence.
- `FORM-ANSWERS.md`: ready-to-paste optional answers.

## Commands

Run from `day-2/task-3`:

```bash
dotnet restore Task3.slnx
dotnet build Task3.slnx --no-restore
dotnet test Tests.Domain/Tests.Domain.csproj --no-build --no-restore
```

## Actual verified results

Verified on August 11, 2026 with .NET SDK 10.0.302:

- Restore succeeded.
- Complete solution build succeeded with 0 warnings and 0 errors.
- Required domain suite: 6 passed, 0 failed, 0 skipped.
- Detailed run: xUnit started the suite at 0.09 seconds and finished at 0.13 seconds, approximately 40 ms of execution; individual test timings totaled approximately 10 ms or less. VSTest's 0.4607-second total also includes discovery and test-host overhead.
- Three additional deterministic runs passed in 13 ms, 18 ms, and 17 ms.
- Static audit confirmed `Tests.Domain` contains no database, network, web factory, dependency-injection, mock, fixture, setup, sleep, or current-time usage.
