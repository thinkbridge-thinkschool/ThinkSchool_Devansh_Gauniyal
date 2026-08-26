# .NET test regression — Phase 6 (re-run after day-15/task-1 work completed)

Full raw output for each solution is under `dotnet-regression/<day-X-task-Y>.txt`.

## Result: 38 of 39 solutions match the Phase 2 baseline exactly. One does not, and it is
## NOT a code regression — see below.

| Solution path | Baseline (P/F/S/T) | This run (P/F/S/T) | Match? |
|---|---|---|---|
| day-1/task-4/Task4.slnx | 4/0/0/4 | 4/0/0/4 | yes |
| day-1/task-5/Task5.sln | 3/0/0/3 | 3/0/0/3 | yes |
| day-1/task-7/Task7.sln | 6/0/0/6 | 6/0/0/6 | yes |
| day-10/task-1/Task1.slnx | 16/0/0/16 | 16/0/0/16 | yes |
| day-10/task-2/Task2.slnx | 15/0/0/15 | 15/0/0/15 | yes |
| day-11/task-1/Task1.slnx | 16/0/0/16 | 16/0/0/16 | yes |
| day-11/task-2/Task2.slnx | 18/0/0/18 | 18/0/0/18 | yes |
| day-12/task-1/Task1.slnx | 25/0/0/25 | 25/0/0/25 | yes |
| day-12/task-2/Task2.slnx | 15/0/0/15 | 15/0/0/15 | yes |
| day-2/task-1/Task1.slnx | 1/0/0/1 | 1/0/0/1 | yes |
| day-2/task-2/Task2.slnx | 7/0/0/7 | 7/0/0/7 | yes |
| day-2/task-3/Task3.slnx | 6/0/0/6 | 6/0/0/6 | yes |
| day-2/task-4/Task4.slnx | 13/0/0/13 | 13/0/0/13 | yes |
| day-2/task-6/Task6.slnx | 16/0/0/16 | 16/0/0/16 | yes |
| day-2/task-7/Task7.slnx | 29/0/0/29 | 29/0/0/29 | yes |
| day-3/task-1/Task1.slnx | 6/0/0/6 | 6/0/0/6 | yes |
| day-3/task-2/Task2.slnx | 7/0/0/7 | 7/0/0/7 | yes |
| day-3/task-3/Task3.slnx | 19/0/0/19 | 19/0/0/19 | yes |
| day-3/task-5/Task5.slnx | 44/0/0/44 | 44/0/0/44 | yes |
| day-3/task-6/Task6.slnx | 22/0/0/22 | 22/0/0/22 | yes |
| **day-3/task-7/Task7.slnx** | **0/15/0/15** | **15/0/0/15** | **NO — see below** |
| day-4/task-1/Task1.slnx | 12/0/0/12 | 12/0/0/12 | yes |
| day-4/task-2/Task2.slnx | 56/0/0/56 | 56/0/0/56 | yes |
| day-4/task-4/Task4.slnx | 58/0/0/58 | 58/0/0/58 | yes |
| day-4/task-5/Task5.slnx | 60/0/0/60 | 60/0/0/60 | yes |
| day-4/task-6/Task6.slnx | 61/0/0/61 | 61/0/0/61 | yes |
| day-4/task-7/Task7.slnx | 73/0/0/73 | 73/0/0/73 | yes |
| day-5/task-1/Task1.slnx | 10/0/0/10 | 10/0/0/10 | yes |
| day-5/task-2/Task2.slnx | 10/0/0/10 | 10/0/0/10 | yes |
| day-5/task-4/Task4.slnx | 10/0/0/10 | 10/0/0/10 | yes |
| day-5/task-5/Task5.slnx | 10/0/0/10 | 10/0/0/10 | yes |
| day-5/task-6/Task6.slnx | 6/0/0/6 | 6/0/0/6 | yes |
| day-7/task-1/Task1.slnx | 11/0/0/11 | 11/0/0/11 | yes |
| day-7/task-2/Task2.slnx | 8/0/0/8 | 8/0/0/8 | yes |
| day-7/task-3/Task3.slnx | 13/0/0/13 | 13/0/0/13 | yes |
| day-8/task-1/Task1.slnx | 96/0/0/96 | 96/0/0/96 | yes |
| day-8/task-2/Task2.slnx | 36/0/0/36 | 36/0/0/36 | yes |
| day-9/task-1/Task1.slnx | 86/0/0/86 | 86/0/0/86 | yes |
| day-9/task-2/Task2.slnx | 71/0/0/71 | 71/0/0/71 | yes |

**Grand totals** — baseline: Passed 975, Failed 15, Total 990. This run: Passed 990,
Failed 0, Total 990.

## The one mismatch: day-3/task-7, and why it is NOT a regression

**Docker state changed mid-session, outside of my control and outside of anything this
task touched.** `docker info` was confirmed failing (Docker down) at the Phase 2
baseline measurement. By the time this Phase 6 regression re-run happened, `docker info`
succeeded with a fully populated server response (Docker Desktop had been started —
observably not by any action in this task, since day-15/task-1's work never touches
Docker, docker-compose, or any Docker Desktop control surface).

`day-3/task-7`'s 15 integration tests (`Quotes.Tests.Integration.dll`, using
Testcontainers) failed with `DockerUnavailableException` in the baseline because the
Docker daemon was unreachable — an explicitly accepted, expected baseline state per this
task's own instructions ("If Docker is down, those failures are the ACCEPTED baseline,
not a regression"). In this re-run, the exact same 15 tests now pass, because
Testcontainers can now reach a running Docker daemon:
`Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15, Duration: 3 s -
Quotes.Tests.Integration.dll (net10.0)` — full output in
`dotnet-regression/day-3-task-7.txt`.

This is a strictly BETTER outcome (0 failures instead of 15), caused entirely by an
external environment change (Docker Desktop being started by someone/something outside
this session) between the two measurements, not by any file this task touched. Nothing
in `day-3/task-7` was modified — confirmed by `git status`/`git diff --stat` showing no
changes anywhere under `day-3/`. I did not start Docker myself; I chose not to stop it
either, since it is shared system state outside this task's scope and stopping it
without being asked risked disrupting unrelated work Devansh may have started it for.

Every other one of the 38 remaining solutions matches its baseline count exactly, byte
for byte in outcome (same Passed/Failed/Skipped/Total). No unexpected failure and no
unexpected pass anywhere else in the repository.
