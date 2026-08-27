# .NET test baseline — Phase 2 (measured before any day-16/task-2 store work)

Docker Desktop state: **up** (`docker info` succeeded) at measurement time. Same state
must be used for the Phase 6 regression re-run.

`dotnet test` run against all 39 real solutions under day-1 through day-9 (day-1's
`Task4.sln`/`Task4.slnx` are the same solution in two formats; only the `.slnx` was run
— same convention day-16/task-1 used). Full raw output for each solution is under
`output/dotnet-baseline/<day-X-task-Y>.txt`.

## Grand totals

**Passed: 990 · Failed: 0 · Skipped: 0 · Total: 990**

Matches day-16/task-1's own Phase 2/Phase 6 baseline exactly (also measured with Docker
up), including all 15 of day-3/task-7's Testcontainers-backed integration tests passing.

## Angular baseline (in place), same measurement pass

| App | Test files | Tests | Result |
|---|---|---|---|
| day-13/task-1 | 3 | 13 | all passed |
| day-13/task-2 | 3 | 15 | all passed |
| day-14/task-1 | 4 | 40 | all passed |
| day-14/task-2 | 6 | 56 | all passed |
| day-15/task-1 | 12 | 79 | all passed |
| day-16/task-1 | 18 | 94 | all passed — this is the app carried into day-16/task-2, and the count the copy had to match (it did, exactly: 18/94, confirmed after copy + npm install, before any store code was added) |

All six counts match each app's own prior recorded baseline exactly, confirming nothing
in day-13 through day-16/task-1 changed in the interim.
