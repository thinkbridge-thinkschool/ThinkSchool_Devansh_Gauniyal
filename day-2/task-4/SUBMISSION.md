# Thinkbridge Submission Pack: Day 2, Task 4

## Branch URL

https://github.com/devansh-gauniyal/thinkschool/tree/day-2/task-4/day-2/task-4

## WHY.md — exactly 200 words

The original Quote model was anemic because public setters allowed a caller to assign any author or text. Validation in an endpoint protected only that endpoint. An import job could bypass those checks and save an invalid quote.

The rich model gives Quote responsibility for its own rules. Quote.Create is the single public entry point for construction. It checks that Author contains 1–200 characters and Text contains 1–1000 characters, rejecting null, empty, whitespace-only, or oversized values with a domain error. Controllers, jobs, and services therefore receive identical validation without duplicating rules.

Private setters keep the entity compatible with EF Core while preventing callers from rewriting state. Text has no public setter or update method, so published wording cannot change. SoftDelete is also a domain operation. Callers ask the quote to delete itself instead of directly setting a flag or physically removing its row. A global query filter keeps deleted quotes out of normal reads.

For example, a CSV importer might create a quote with 1,001 text characters because it forgot controller validation, then later overwrite Text after publication. Now the importer must call Quote.Create, which returns a domain error for that input, and immutable Text prevents the second bug completely.

Verified word count: `200` using `wc -w WHY.md`.

## Refactor summary

- Replaced public Quote setters with private setters and an EF-compatible private constructor.
- Added `Quote.Create(author, text)` with 1–200 author and 1–1000 text invariants.
- Added the focused `QuoteCreationResult` and `DomainError` types instead of a generic framework.
- Removed controller-only validation and object binding to the entity.
- Mapped factory failures to HTTP 400 validation details with stable error codes.
- Preserved the injected clock and propagated cancellation through all repository I/O.
- Replaced physical deletion with `Quote.SoftDelete()` and persistence of `IsDeleted`.
- Added a global EF Core query filter so normal reads hide deleted quotes.
- Added a Task 4-only migration without changing historical migrations.
- Added 13 pure domain test cases covering all validation boundaries, immutability, and deletion.

## Important changed files

- `QuotesApi/Models/Quote.cs`
- `QuotesApi/Models/QuoteCreationResult.cs`
- `QuotesApi/Models/DomainError.cs`
- `QuotesApi/Dtos/CreateQuoteRequest.cs`
- `QuotesApi/Program.cs`
- `QuotesApi/Repositories/IQuoteRepository.cs`
- `QuotesApi/Repositories/QuoteRepository.cs`
- `QuotesApi/Data/QuotesDbContext.cs`
- `QuotesApi/Migrations/20260811064055_MakeQuoteRichAndSoftDeletable.cs`
- `Tests.Domain.Quotes/QuoteTests.cs`
- `WHY.md`

## Actual verification results

- Restore: succeeded.
- Final build: succeeded with 0 warnings and 0 errors in 0.50 seconds.
- Full test suite: 13 passed, 0 failed, 0 skipped, 27 ms reported duration.
- Additional deterministic test runs: 23 ms and 26 ms; both passed 13/13.
- API startup: all three migrations applied to a fresh temporary SQLite database; no EF Core or DI errors.
- Valid create: HTTP 201 with injected UTC timestamp and `isDeleted: false`.
- Invalid whitespace author: HTTP 400 with `quote.author.required`.
- Invalid whitespace text: HTTP 400 with `quote.text.required`.
- Delete: HTTP 204; subsequent get returned 404 and list returned `[]`.
- Raw SQLite row after delete: `1|Grace Hopper|A ship in port is safe.|1`, proving the row remained and the flag changed.
- Shutdown: clean, exit code 0.
- Scope audit: no Day 1 or earlier Day 2 task changed.

## Git evidence

- Branch: `day-2/task-4`
- Implementation commit: `c593b0575d6b4f04fd18c7ee9327774476b8fed1`
