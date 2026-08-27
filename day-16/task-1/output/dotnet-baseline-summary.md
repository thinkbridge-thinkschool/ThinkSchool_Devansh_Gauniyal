# .NET test baseline — Phase 2 (measured before any day-16/task-1 routing work)

Docker Desktop state: **up** (`docker info` succeeded) at measurement time. Same state
must be used for the Phase 6 regression re-run.

`dotnet test` run against all 39 real solutions under day-1 through day-9 (day-1's
`Task4.sln`/`Task4.slnx` are the same solution in two formats; only the `.slnx` was run
— same convention day-15/task-1 used for its own baseline). Full raw output for each
solution is under `output/dotnet-baseline/<day-X-task-Y>.txt`; the exact per-solution
tail lines are in `output/dotnet-baseline/summary.md` (unformatted capture from the run
itself).

## Grand totals

**Passed: 990 · Failed: 0 · Skipped: 0 · Total: 990**

Docker was running for this measurement, so day-3/task-7's 15 Testcontainers-backed
integration tests passed rather than failing with `DockerUnavailableException` (they
failed all 15 in day-15/task-1's own baseline, where Docker was down — same tests,
different Docker state, not a discrepancy). This 990/990 figure, with Docker **up**, is
this task's baseline; Phase 6 must reproduce it with Docker in the same (up) state.

## Angular baseline (in place), same measurement pass

| App | Test files | Tests | Result |
|---|---|---|---|
| day-13/task-1 | 3 | 13 | all passed |
| day-13/task-2 | 3 | 15 | all passed |
| day-14/task-1 | 4 | 40 | all passed |
| day-14/task-2 | 6 | 56 | all passed |
| day-15/task-1 | 12 | 79 | all passed — this is the app carried into day-16/task-1, and the count the copy had to match (it did, exactly: 12/79, confirmed after copy + npm install, before any routing code was added) |

All five counts match day-15/task-1's own recorded baseline for the same four earlier
apps exactly (day-15/task-1/output/dotnet-baseline-summary.md), confirming nothing in
day-13 or day-14 changed in the interim.
