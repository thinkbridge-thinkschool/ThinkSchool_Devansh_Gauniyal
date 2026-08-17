## GitHub link
https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-7/task-2/day-7/task-2

## Notes for mentor

```sql
-- THE GRADED QUERY.
-- One row per quote (not one row per author -- that was Day 7 Task 1's graded query). Per
-- author: quote text, its timestamp, a running count of quotes up to and including this
-- one, the previous quote's CreatedAt via LAG, and the gap since it in days (raw + rounded).
--
-- The window is declared ONCE via the named WINDOW clause below and reused by every
-- column that needs it, rather than repeating the same OVER (...) spec on each one.
--
-- Id is included in the window's ORDER BY as the deterministic tie-break: Talia Marsh's
-- Quotes 6 and 7 share an identical CreatedAt, and without Id here their relative order
-- (and so which one "comes after" the other for LAG/running-count purposes) would be
-- arbitrary. With it, Quote 6 always precedes Quote 7, on every run, on every SQLite build.
SELECT
    a.Name AS AuthorName,
    q.Text AS QuoteText,
    q.CreatedAt,
    COUNT(*) OVER AuthorWindow AS RunningQuoteCount,
    LAG(q.CreatedAt) OVER AuthorWindow AS PreviousQuoteCreatedAt,
    -- SQLite has no DATEDIFF; T-SQL would write DATEDIFF(day, PreviousQuoteCreatedAt, CreatedAt).
    -- The first quote per author has no previous row, so LAG returns NULL and this
    -- subtraction propagates NULL rather than becoming 0 -- see the comment below on why
    -- that distinction matters.
    julianday(q.CreatedAt) - julianday(LAG(q.CreatedAt) OVER AuthorWindow) AS GapDaysRaw,
    -- The NULL-vs-0 distinction must survive rounding too: a first quote should report "no
    -- previous quote to compare to" (NULL), not "zero days since the last one" (0) -- a
    -- report that silently coerced this to 0 would claim every author's opening quote was
    -- itself a same-day repeat. ROUND(NULL) is itself NULL in SQLite, so this falls out for
    -- free rather than needing a special case.
    ROUND(
        julianday(q.CreatedAt) - julianday(LAG(q.CreatedAt) OVER AuthorWindow)
    , 0) AS GapDaysRounded
FROM Quotes q
INNER JOIN Authors a ON a.Id = q.AuthorId
WINDOW AuthorWindow AS (
    PARTITION BY q.AuthorId ORDER BY q.CreatedAt, q.Id
    ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
)
ORDER BY a.Name, q.CreatedAt, q.Id;
```

SAMPLE ROWS (~12-15): Callum Reyes (single-quote author), Talia Marsh (tied timestamps), Wren Ashby (same-day / few-days / year-boundary gaps), and Dorian Fenwick's first 4 (ordinary rows).
```
AuthorName      QuoteText                                                           CreatedAt            RunningQuoteCount  PreviousQuoteCreatedAt  GapDaysRaw        GapDaysRounded
--------------  ------------------------------------------------------------------  -------------------  -----------------  ----------------------  ----------------  --------------
Callum Reyes    A single note, played well, outlasts a careless symphony.           2023-07-02 09:00:00  1
Dorian Fenwick  Every ledger eventually asks to be read aloud.                      2023-01-05 08:00:00  1
Dorian Fenwick  A door left ajar is not the same as an invitation.                  2023-01-20 10:00:00  2                  2023-01-05 08:00:00     15.0833333330229  15.0
Dorian Fenwick  The quiet ones keep the loudest records.                            2023-02-10 09:00:00  3                  2023-01-20 10:00:00     20.9583333334886  21.0
Dorian Fenwick  Debt is just memory with interest.                                  2023-03-01 11:00:00  4                  2023-02-10 09:00:00     19.0833333334886  19.0
Talia Marsh     Attention paid freely is the only gift that costs everything.       2023-04-10 12:00:00  1
Talia Marsh     Two clocks can strike the same hour and still disagree about time.  2023-04-10 12:00:00  2                  2023-04-10 12:00:00     0.0               0.0
Talia Marsh     Light borrowed is still light.                                      2023-08-20 09:00:00  3                  2023-04-10 12:00:00     131.875           132.0
Wren Ashby      The map is never the mountain, only a promise about the mountain.   2023-06-01 09:00:00  1
Wren Ashby      Patience is a room you build one plank at a time.                   2023-06-01 15:00:00  2                  2023-06-01 09:00:00     0.25              0.0
Wren Ashby      A held breath teaches more than a shouted answer.                   2023-06-05 10:00:00  3                  2023-06-01 15:00:00     3.79166666651145  4.0
Wren Ashby      Winter asks the same questions summer avoided.                      2024-01-10 08:00:00  4                  2023-06-05 10:00:00     218.916666666977  219.0
Wren Ashby      Small repairs, done early, prevent large collapses.                 2024-01-15 08:00:00  5                  2024-01-10 08:00:00     5.0               5.0
```

Engine: SQLite, chosen because SQL Server container images are x86_64-only and won't run natively on this arm64 Mac. SQLite has no `DATEDIFF`, so day gaps use `julianday(a) - julianday(b)` subtraction instead, rounded with `ROUND(...)` where a whole-day count is needed. This query is one row **per quote**, whereas Day 7 Task 1's graded query collapsed to one row per author. The `ROW_NUMBER`/`RANK`/`DENSE_RANK` comparison is in `sql/10_row_number_vs_rank.sql`, and the `RANGE`-vs-`ROWS` running-total comparison is in `sql/12_running_total.sql`.

## What did you learn this session?
I learned that the default window frame is `RANGE`, not `ROWS`, and that `RANGE` silently groups every row tied on the `ORDER BY` value together, giving them all the same running total instead of incrementing one at a time. I also learned a named `WINDOW` clause lets several columns share one window definition instead of repeating the same `OVER (...)` on each.

## What would break this?
Relying on the default `RANGE` frame anywhere ties are possible would double-count tied rows into the same running total instead of counting them one at a time. Storing `CreatedAt` in a format `julianday()` can't parse, or dropping `Id` from the window's `ORDER BY`, would silently break the day-gap math and make the tie-break non-deterministic.
