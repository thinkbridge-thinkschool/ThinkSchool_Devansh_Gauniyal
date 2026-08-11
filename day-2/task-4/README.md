# Day 2, Task 4: Refactor Quote from Anemic to Rich

## Anemic and rich domain models

The original `Quote` was anemic: it exposed public setters and contained no business rules. Any endpoint, job, test, or deserializer could create an invalid quote or replace its text after publication.

The rich model owns its invariants and state transitions. Callers cannot construct `Quote` directly. They use `Quote.Create(author, text)`, receive either a valid quote or a small `DomainError`, and can only delete through `SoftDelete()`.

## Invariants and factory construction

`Quote.Create` is the single normal construction path. It enforces:

- Author is not null, empty, or whitespace-only.
- Author contains at most 200 characters; one character is valid.
- Text is not null, empty, or whitespace-only.
- Text contains at most 1,000 characters; one character is valid.

The factory does not throw for expected validation failures. `QuoteCreationResult` is deliberately quote-specific instead of a generic result framework. On success it contains `Value`; on failure it contains a stable error code and useful message.

## Immutability

`Id`, `Author`, `Text`, `CreatedAtUtc`, and `IsDeleted` have private setters. EF Core can still materialize the entity through its private parameterless constructor, while normal application callers cannot use object initializers or setters. There is no `UpdateText` method, so Text has no supported mutation path after creation.

The repository retains Task 1's injected `IClock` behavior and sets `CreatedAtUtc` through an internal persistence method. It also receives cancellation tokens for all database I/O.

## Soft deletion

`SoftDelete()` changes `IsDeleted` inside the domain model. The repository saves that update instead of calling `Remove`. `QuotesDbContext` defines a global query filter, so normal list and get queries automatically hide deleted quotes.

The Task 4 migration adds only the non-null `IsDeleted` column with a default of `false`. Historical migrations remain unchanged.

## Endpoint mapping

`POST /api/quotes` accepts a `CreateQuoteRequest` DTO and calls `Quote.Create`. A domain failure becomes HTTP 400 validation details keyed by the stable error code. A successful result is persisted and returns the existing HTTP 201 shape.

`DELETE /api/quotes/{id}` calls the repository, which obtains the quote, invokes `SoftDelete`, and saves it. Existing routes remain unchanged.

## Tests

`Tests.Domain.Quotes` uses xUnit and Fluent Assertions without a database, network, web factory, fixtures, or current time. Its 13 executed cases cover:

- one-character valid values;
- null, empty, and whitespace-only text;
- exactly 1,000 and 1,001 text characters;
- null, empty, and whitespace-only author;
- exactly 200 and 201 author characters;
- the soft-delete flag; and
- absence of a public Text setter or update method.

## Important files

- `QuotesApi/Models/Quote.cs`: rich entity, factory, invariants, and soft deletion.
- `QuotesApi/Models/QuoteCreationResult.cs`: focused success-or-error result.
- `QuotesApi/Models/DomainError.cs`: stable validation error.
- `QuotesApi/Dtos/CreateQuoteRequest.cs`: HTTP input shape.
- `QuotesApi/Program.cs`: factory and HTTP error mapping.
- `QuotesApi/Repositories/QuoteRepository.cs`: timestamping, cancellation, and soft-delete persistence.
- `QuotesApi/Data/QuotesDbContext.cs`: constraints and deleted-row query filter.
- `QuotesApi/Migrations/20260811064055_MakeQuoteRichAndSoftDeletable.cs`: Task 4 schema change.
- `Tests.Domain.Quotes/QuoteTests.cs`: focused rich-domain tests.
- `WHY.md`: exact 200-word Academy explanation.

## Actual verification

Verified on August 11, 2026 with .NET SDK 10.0.302 and EF Core tools 10.0.10:

- Restore succeeded.
- Build succeeded with 0 warnings and 0 errors.
- All 13 domain cases passed; the detailed VSTest run reported 0.3645 seconds total.
- Two additional runs passed in 23 ms and 26 ms.
- Fresh-database startup applied all three migrations without EF or DI errors.
- Valid creation returned 201 with `isDeleted: false` and an injected UTC timestamp.
- Whitespace author and whitespace text each returned 400 with stable domain errors.
- Delete returned 204; subsequent get returned 404 and list returned an empty array.
- Raw SQLite verification showed the row still existed with `IsDeleted = 1`.
- API shutdown was clean with exit code 0.
