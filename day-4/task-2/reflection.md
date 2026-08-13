# Reflection draft — Day 4 Task 2

**Which uncovered branch surprised you most, and what did you learn from covering it?**

Draft paragraph (for you to edit into your own words before submitting):

> The most surprising gap wasn't a subtle edge case — it was that `InMemoryQuoteRepository.GetAll()`
> had zero coverage because no test ever called it, and the reason turned out to be that
> `GET /api/quotes` (`Program.cs:189-190`) has no `.RequireAuthorization()` at all, unlike every
> other endpoint on that same resource. I went looking for untested branches and found an
> inconsistent security posture instead. It taught me that a coverage report doesn't just point
> at missing tests — reading *why* a line is never hit is sometimes the more valuable signal,
> since "nobody wrote a test for this" and "this code path doesn't behave like its siblings" can
> be the same finding wearing different clothes. The second thing that stuck with me: almost every
> other real gap (five separate guard clauses in `InternalJwtOptions` alone) was the same shape —
> a validation `throw` that only fires on bad *configuration*, which a test factory that always
> hands the app valid settings will never exercise no matter how many HTTP-level tests you add.
> Covering those needed a completely different kind of test (plain unit tests against the options
> class directly, no server involved) than the ownership/authentication edge cases did.

Notes for context (not for the form, just so you remember the specifics while rewriting this):
- The public `GET /api/quotes` finding was raised as a question before writing any test for it;
  the test documents current behavior only, it doesn't assert the behavior is correct.
- The `InternalJwtOptions` validation gaps were the single largest untested surface by line count.
- One additional finding along the way: `RefreshTokenService.StoredRefreshToken.TokenHash` was
  assigned but never read anywhere — genuine dead code, removed with approval, not tested around.
