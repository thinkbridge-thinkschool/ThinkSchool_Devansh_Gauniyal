# Day 4 — Task 2: Drive yesterday's auth codebase to 80% coverage

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-2/day-4/task-2

## Coverage report

```
Baseline (existing 19 tests):      90.20% line (433/480), 66.25% branch (53/80)
Final (19 existing + 37 new tests): 100.00% line (475/475), 0 lines uncovered

Union line coverage:   475/475 = 100.00%
Still-uncovered lines after merge: (none)
```

Full per-class breakdown: `day-4/task-2/coverage-summary.txt`.

## Notes for your mentor

Baseline was 90.20% line / 66.25% branch (day-3/task-3/QuotesApi.Tests, 19 tests — added coverlet.collector there so it could be measured at all, no test logic changed). Added 37 new tests in a fresh project (day-4/task-2/QuotesApi.Auth.Tests, ProjectReference only, no Day 3 source duplicated) covering every guard clause and edge case the baseline flagged as uncovered. Also removed one dead property (RefreshTokenService.TokenHash — assigned, never read). Combined: 100.00% line coverage, merged honestly by line union across both test projects. CI run (Task 1's pipeline, unaffected): https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs/31674741291

## What did you learn this session?

The most surprising gap wasn't a subtle edge case — `InMemoryQuoteRepository.GetAll()` had zero coverage because `GET /api/quotes` has no `.RequireAuthorization()` at all, unlike every other endpoint on that resource. A missing test and an inconsistent security posture turned out to be the same finding. I also learned that unit tests and integration tests cover genuinely different failure surfaces: five of the largest gaps were config-validation guard clauses (`InternalJwtOptions.ValidateAndGetSigningKey()`) that no HTTP-level test could ever reach, because the test factory always supplies valid config — those needed direct unit tests, not more HTTP requests. Finally, merging coverage across two test projects hitting the same DLL isn't addition — it's a union of covered lines, since summing would double-count the shared denominator.

## What would break this?

`GetQuotes_Anonymous_ReturnsOkWithAllQuotes` locks in the current unauthenticated behavior of `GET /api/quotes` — if that missing auth check is actually a bug rather than intentional, this test breaks the moment it's fixed (which is the point, but it means the assertion is tied to a decision that hasn't been made yet). Branch coverage also can't be proven merged as rigorously as line coverage: this Cobertura output doesn't expose per-line branch attribution, so I can confirm no *line* is dark but not that every individual branch *outcome* is covered across both suites combined. And the "unknown ID" tests (999999) are implicitly coupled to today's seed data (ids 1, 2) — they'd fail safely, not silently, if that ever changes, but they'd need a look.
