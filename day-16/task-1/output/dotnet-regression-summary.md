# .NET test regression — Phase 6 (measured after day-16/task-1's routing work)

Docker Desktop state: **up** — same as the Phase 2 baseline measurement.

`dotnet test` re-run against the identical 39 solutions as the Phase 2 baseline. Full raw
output for each solution is under `output/dotnet-regression/<day-X-task-Y>.txt`.

## Grand totals

**Passed: 990 · Failed: 0 · Skipped: 0 · Total: 990**

Identical to the Phase 2 baseline (990/990, Docker up) — every solution's per-test
Passed/Failed/Skipped counts match exactly, solution for solution. Zero regressions
anywhere in day-1 through day-9 as a result of this task's work, which only ever touched
files under `day-16/task-1`.

## Angular regression (in place), same measurement pass

| App | Test files | Tests | Result | vs. Phase 2 baseline |
|---|---|---|---|---|
| day-13/task-1 | 3 | 13 | all passed | matches |
| day-13/task-2 | 3 | 15 | all passed | matches |
| day-14/task-1 | 4 | 40 | all passed | matches |
| day-14/task-2 | 6 | 56 | all passed | matches |
| day-15/task-1 | 12 | 79 | all passed | matches |

No regression anywhere. `git status` / `git diff --stat` confirm nothing outside
`day-16/task-1` changed (the same seven pre-existing unrelated modifications from the
start of this session remain, untouched — see submission.md and the top-level session
transcript for the list).
