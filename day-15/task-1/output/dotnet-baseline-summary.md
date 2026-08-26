# .NET test baseline — Phase 2 (measured before any day-15/task-1 work)

Docker Desktop state: **down** (`docker info` failed) at measurement time. Same state
must be used for the Phase 6 regression re-run.

`dotnet test` run against all 39 `.sln`/`.slnx` files under day-1 through day-9 (day-1's
`Task4.sln`/`Task4.slnx` are the same solution in two formats; only the `.slnx` was run).
Full raw output for each solution is under `dotnet-baseline/<day-X-task-Y>.txt`.

| Solution path | Passed | Failed | Skipped | Total | Notes |
|---|---|---|---|---|---|
| day-1/task-4/Task4.slnx | 4 | 0 | 0 | 4 | |
| day-1/task-5/Task5.sln | 3 | 0 | 0 | 3 | |
| day-1/task-7/Task7.sln | 6 | 0 | 0 | 6 | |
| day-10/task-1/Task1.slnx | 16 | 0 | 0 | 16 | |
| day-10/task-2/Task2.slnx | 15 | 0 | 0 | 15 | |
| day-11/task-1/Task1.slnx | 16 | 0 | 0 | 16 | |
| day-11/task-2/Task2.slnx | 18 | 0 | 0 | 18 | |
| day-12/task-1/Task1.slnx | 25 | 0 | 0 | 25 | |
| day-12/task-2/Task2.slnx | 15 | 0 | 0 | 15 | |
| day-2/task-1/Task1.slnx | 1 | 0 | 0 | 1 | |
| day-2/task-2/Task2.slnx | 7 | 0 | 0 | 7 | |
| day-2/task-3/Task3.slnx | 6 | 0 | 0 | 6 | |
| day-2/task-4/Task4.slnx | 13 | 0 | 0 | 13 | |
| day-2/task-6/Task6.slnx | 16 | 0 | 0 | 16 | |
| day-2/task-7/Task7.slnx | 29 | 0 | 0 | 29 | |
| day-3/task-1/Task1.slnx | 6 | 0 | 0 | 6 | |
| day-3/task-2/Task2.slnx | 7 | 0 | 0 | 7 | |
| day-3/task-3/Task3.slnx | 19 | 0 | 0 | 19 | |
| day-3/task-5/Task5.slnx | 44 | 0 | 0 | 44 | |
| day-3/task-6/Task6.slnx | 22 | 0 | 0 | 22 | |
| day-3/task-7/Task7.slnx | 0 | 15 | 0 | 15 | Docker-dependent (Testcontainers); Docker was down; all 15 failures are DockerUnavailableException — accepted baseline, not a regression |
| day-4/task-1/Task1.slnx | 12 | 0 | 0 | 12 | |
| day-4/task-2/Task2.slnx | 56 | 0 | 0 | 56 | 2 test projects |
| day-4/task-4/Task4.slnx | 58 | 0 | 0 | 58 | 3 test projects |
| day-4/task-5/Task5.slnx | 60 | 0 | 0 | 60 | 4 test projects |
| day-4/task-6/Task6.slnx | 61 | 0 | 0 | 61 | 5 test projects |
| day-4/task-7/Task7.slnx | 73 | 0 | 0 | 73 | 6 test projects |
| day-5/task-1/Task1.slnx | 10 | 0 | 0 | 10 | |
| day-5/task-2/Task2.slnx | 10 | 0 | 0 | 10 | |
| day-5/task-4/Task4.slnx | 10 | 0 | 0 | 10 | |
| day-5/task-5/Task5.slnx | 10 | 0 | 0 | 10 | |
| day-5/task-6/Task6.slnx | 6 | 0 | 0 | 6 | |
| day-7/task-1/Task1.slnx | 11 | 0 | 0 | 11 | |
| day-7/task-2/Task2.slnx | 8 | 0 | 0 | 8 | |
| day-7/task-3/Task3.slnx | 13 | 0 | 0 | 13 | |
| day-8/task-1/Task1.slnx | 96 | 0 | 0 | 96 | |
| day-8/task-2/Task2.slnx | 36 | 0 | 0 | 36 | |
| day-9/task-1/Task1.slnx | 86 | 0 | 0 | 86 | |
| day-9/task-2/Task2.slnx | 71 | 0 | 0 | 71 | |

## Grand totals

**Passed: 975 · Failed: 15 · Skipped: 0 · Total: 990**

All 15 failures are `DockerUnavailableException` from day-3/task-7 (Docker was down).
Zero unexpected failures anywhere else in the repository; no build failures anywhere.
This is the baseline Phase 6 must reproduce exactly, with Docker in the same (down) state.

## Angular baseline (in place), same measurement pass

| App | Test files | Tests | Result |
|---|---|---|---|
| day-13/task-1 | 3 | 13 | all passed |
| day-13/task-2 | 3 | 15 | all passed |
| day-14/task-1 | 4 | 40 | all passed |
| day-14/task-2 | 6 | 56 | all passed — this is the app carried into day-15/task-1, and the count the copy had to match (it did, exactly: 6/56, confirmed after copy + npm install) |
