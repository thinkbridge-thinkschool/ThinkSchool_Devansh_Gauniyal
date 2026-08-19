# Day 9 / Task 2 — Reproduce and resolve a deadlock

## What a deadlock is

A deadlock is a circular wait: two (or more) transactions each hold a lock
the other one needs, and each is blocked waiting for the other to release
it. Neither can proceed, so left alone they would wait forever. SQL Server
runs a background deadlock monitor that periodically scans the lock graph
for cycles; when it finds one, it picks one of the participating sessions
as the *victim*, kills that session's transaction (rolling it back) and
returns error **1205** to it. The other session's blocked request can then
complete normally.

This is different from ordinary blocking. Blocking is one session waiting
for a lock another session will *eventually* release — it resolves itself
once the lock-holder commits or rolls back. A deadlock is blocking that can
never resolve itself, because the wait forms a cycle. SQL Server has no
special detector for "blocking" — only for cycles.

## The exact circular-wait ordering used

Two tables, `dbo.Accounts` and `dbo.Orders`, each with a row `Id = 1`:

1. **Session A** begins a transaction and updates `Accounts.Id = 1` — it now
   holds an exclusive lock on that row.
2. **Session B** begins a transaction and updates `Orders.Id = 1` — it now
   holds an exclusive lock on that row.
3. **Session A** updates `Orders.Id = 1` — Session B holds that lock, so A
   blocks.
4. **Session B** updates `Accounts.Id = 1` — Session A holds that lock, so B
   blocks.

Steps 3 and 4 form the cycle: A is waiting on B, and B is waiting on A.
Neither can make progress on its own. SQL Server's deadlock monitor detects
this and kills one session with error 1205; the other's blocked update then
completes and it commits.

The scripts are in `sql/`: `10_deadlock_sessionA.sql` and
`11_deadlock_sessionB.sql`. Session A always touches Accounts before Orders;
Session B, in the broken version, touches Orders before Accounts — the
reverse of A. That reversal is the entire cause of the deadlock.

## Why the orchestrator fires both blocking statements without waiting

`scripts/run-experiment.sh` drives two long-lived `sqlcmd` sessions from one
bash process, each attached to its own named pipe (`mkfifo`), so it can send
a session a batch at the exact moment it should run and read that session's
transcript to know when a step finished.

Steps 3 and 4 above are the sequencing problem: once both statements are
sent, *neither will return on its own* until the deadlock monitor breaks the
cycle. If the orchestrator sent Session A's statement and waited for it to
finish before sending Session B's, it would block forever — A can never
return without B's statement being sent first (only Session B's request
releases the resource A is contending over, indirectly, by giving the
deadlock monitor two blocked requests to find a cycle in; a lone blocked
request is just ordinary blocking with no cycle to detect).

So the orchestrator sends A's request and only waits for a "the server has
started processing this batch" marker (printed *before* the request), not
for the request itself to finish. It then immediately does the same for
B's request. Only after *both* have been dispatched does it wait — with a
generous timeout — for each session's "this batch finished" marker, which
fires once the deadlock monitor has resolved the cycle one way or the
other. This is `send_only_with_start_mark` / `wait_for_mark` in the script.

## Why `LOCK_TIMEOUT` must not be set here

If either session had `SET LOCK_TIMEOUT` in effect, a blocked request could
time out (error **1222** — see the disambiguation below) before SQL
Server's own deadlock monitor ever ran. That would produce a lock timeout,
which looks superficially similar (a session gives up and returns an error)
but is not a deadlock: no cycle was necessarily present, and no victim was
chosen by the deadlock monitor. This experiment needs a genuine circular
wait to be resolved by the deadlock monitor itself, so both blocking
statements are sent with no lock timeout in effect, and the orchestrator's
own wait (`wait_for_mark ... 60`) is generous enough that it does not
race the server's own detection interval.

## Why victim selection is non-deterministic

SQL Server chooses the deadlock victim by estimated rollback cost (roughly:
whichever transaction is cheaper to undo), not by which session issued its
blocking request first or by connection order. Which of Session A or
Session B receives error 1205 can therefore differ between runs of the
identical scripts. The verification tests assert *exactly one of the two
sessions* reports error 1205 without hardcoding which one, and
`output/10_deadlock_broken/victim.txt` records which session was the victim
in the specific run that was captured for this submission.

## Number disambiguation

| Term | Meaning |
|---|---|
| Trace flag 1222 | A diagnostic flag (`DBCC TRACEON (1222, -1)`) that makes SQL Server write a full deadlock graph to the error log whenever the deadlock monitor resolves a deadlock. |
| Error 1205 | "Transaction was deadlocked on lock resources... chosen as the deadlock victim." The error the victim session receives. **This is the signal a genuine deadlock actually occurred.** |
| Error 1222 | "Lock request time out period exceeded." A **lock timeout**, unrelated to trace flag 1222 despite sharing the number. This is *not* a deadlock — it is what happens when `LOCK_TIMEOUT` is set and a wait exceeds it before any deadlock monitor cycle runs. |

If only error 1222 shows up anywhere in the broken-ordering transcripts,
that would mean the repro produced blocking, not a deadlock, and the task
would not be satisfied — the verification tests explicitly check that
1222 does **not** appear in the broken run.

## Two capture routes

Both are captured for every deadlock, because each is a fallback for the
other:

- **Extended Events** (`sql/30_capture_deadlock_xevents.sql`): SQL Server's
  default `system_health` session records every deadlock in its ring
  buffer, independent of any trace flag. The captured `xml_deadlock_report`
  event is saved to `output/deadlock_graph.xdl`.
- **Trace flag 1222** (`sql/31_capture_deadlock_errorlog.sql`): with the
  flag enabled, SQL Server also writes the deadlock graph into its own
  error log — not as angle-bracket XML, but as the same element/attribute
  structure flattened into indented plain-text log lines (`deadlock-list`,
  `process id=... waitresource=...`, `keylock hobtid=... objectname=...`,
  and so on). That block is extracted to
  `output/errorlog_deadlock_report.txt`. The flag is turned back off
  (`sql/04_disable_traceflag_1222.sql`) and confirmed off once the capture
  is done.

## The fix: consistent lock ordering

`sql/20_fixed_sessionA.sql` is identical to `10_deadlock_sessionA.sql` in
every respect — Session A's order was never the problem. The only change is
in `sql/21_fixed_sessionB.sql`: the two `UPDATE` statements from
`11_deadlock_sessionB.sql` swap position, so Session B now also touches
Accounts before Orders. Nothing else changes — no isolation-level change,
no added hint, no retry loop, no `SET DEADLOCK_PRIORITY`. With both sessions
agreeing on the order, Session B's first request now targets a resource
Session A already holds, so it simply queues behind Session A (ordinary
blocking) instead of being able to grab an independent resource first —
there is no second, independent lock for a cycle to form around. Both
sessions complete.

**Why this generalises**: a deadlock requires a cycle in the wait-for
graph. If every transaction acquires resources in the same global order,
no transaction can ever hold a *later* resource while waiting on an
*earlier* one, so no cycle can ever form.

## Schema

`DeadlockLab` has two tables, not one: the exercise's "two-resource" phrase
is the operative interpretation, and two distinct tables make the
resource-acquisition order in each script explicit and readable, which is
the whole point of the exercise. `dbo.Accounts` and `dbo.Orders` each hold a
couple of small, obviously synthetic rows (`Account 0001`,
`Synthetic order row`) — no real names, emails, or account numbers. Row
counts are small deliberately: this is about lock ordering, not volume.

## Rosetta-emulation caveat

Deadlock graphs, Extended Events, trace flag 1222, and the deadlock monitor
itself are all SQL Server engine features, not CPU-architecture features.
This experiment runs SQL Server 2022 (Developer edition) in Docker forced
to `--platform linux/amd64` under Docker Desktop's Rosetta emulation on
Apple Silicon — a configuration Microsoft does not officially support for
production, but a widely used development setup. Lock management and
deadlock detection are engine behaviour, so the demonstration here is
genuine; nothing about it depends on native vs. emulated CPU execution.

## Re-running it

```
./scripts/run-experiment.sh
```

The script is fully self-contained and re-runnable from a clean state:

- generates its own random SA password at runtime (via `openssl rand`),
  held only in a shell variable for the duration of the run — never
  printed, written to a file, or put in a filename
- starts (or replaces) a container named `day9-deadlock-sql` on the first
  free host port from 1434 upward (port 1433 was already in use by an
  earlier day's container)
- polls with a real `SELECT 1` until the server actually accepts
  connections, rather than sleeping a fixed duration
- resets the seed data between the broken and fixed runs so neither run can
  contaminate the other
- has a portable wall-clock guard (macOS ships no `timeout` by default):
  if the whole run wedges, it is killed and the container removed rather
  than hanging indefinitely
- stops and removes its own container when done

## Layout

```
sql/            numbered, idempotent, independently runnable T-SQL scripts
scripts/        the bash orchestrator
output/         real captured transcripts, SPIDs, victim record, deadlock
                graph (.xdl) and error-log capture from the run this
                submission documents
tests/          an offline xunit project that validates the artefacts on
                disk — it never connects to SQL Server, Docker, or the
                network
```
