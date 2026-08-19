# Day 9 / Task 1 — Isolation levels and the read anomalies

Reproduces a dirty read, a non-repeatable read, and a phantom read against
SQL Server 2022 (Docker, `linux/amd64` under Rosetta emulation on Apple
Silicon), and shows which isolation level prevents each one.

## The three anomalies

**Dirty read** — one transaction reads a row another transaction has
changed but not yet committed. If the writer rolls back, the reader saw a
value that never really existed. Prevented once the reader stops taking
uncommitted data at face value, i.e. at READ COMMITTED.

**Non-repeatable read** — a transaction reads the same row twice and gets
two different values, because another transaction updated and committed it
in between. Prevented once the reader holds its locks for the rest of its
own transaction instead of releasing them after each statement, i.e. at
REPEATABLE READ.

**Phantom read** — a transaction re-runs the same range query and a row
appears (or disappears) that wasn't there the first time, because another
transaction inserted (or deleted) a row inside that range. REPEATABLE READ
does not stop this: it only locks rows it has already read, not the gaps
between them. Stopping a phantom means locking the *range itself*, which is
what SERIALIZABLE does.

That distinction — REPEATABLE READ protects rows you've already touched,
SERIALIZABLE protects a whole range against rows that don't exist yet — is
the actual point of the exercise, not just the name of a stricter setting.

## Why named pipes, not sleep or WAITFOR

Two sessions need their statements to interleave in an exact, specific
order (A updates before B reads; B commits only after A has been given a
real chance to time out). `WAITFOR DELAY N` or a bare `sleep N` guesses how
long the other side needs and hopes it guessed right — it can produce a
run that *looks* like it demonstrated something while actually just being
lucky (or unlucky) about timing, and it says nothing about what genuinely
happened.

Instead, `scripts/run-experiment.sh` drives two long-lived `sqlcmd`
processes (one per session) through named pipes (`mkfifo`). Each session is
started as `docker exec -i ... sqlcmd ... < fifo > transcript`, and the
orchestrator holds the pipe's write end open with `exec 3>fifo` /
`exec 4>fifo` for the life of the run. Sending a statement is a `printf`
into that file descriptor; the orchestrator appends its own
`PRINT 'MARK:<name>'` after each statement and then polls the session's
transcript file for that literal marker before deciding what to send next.
That is the actual synchronization primitive: not "wait a while and hope,"
but "wait until the transcript proves the prior step genuinely executed."
The only exception is a step expected to block (a writer waiting on a
reader's lock) — there the orchestrator deliberately does *not* wait for
that step's marker before sending the next session's statement, because
waiting would deadlock the very thing being demonstrated.

One subtlety this surfaced during testing: two *fire-and-forget* sends in a
row (session B's dirty-read attempt, immediately followed by session A's
rollback) are not automatically ordered just because the orchestrator wrote
them in that order — they run on independent connections, and nothing
stops A's rollback from being processed before B's read is even dispatched.
The fix used here is a "start marker" printed *before* the statement that
might block, so the orchestrator can confirm the server has begun that
batch before releasing the other session. Statements within one batch on
one connection always execute in order, so once the start marker is seen,
the following statement is guaranteed to run next on that connection,
uninterrupted by anything on the other one.

## Why a lock timeout counts as proof of prevention

Under a preventing isolation level, the session that would otherwise cause
the anomaly does not get to run to completion — it blocks. Left alone it
would block forever, so every session in this experiment runs with
`SET LOCK_TIMEOUT 5000` (5 seconds). When the anomaly is genuinely
prevented, the blocked statement either:

- eventually completes once the other session commits and releases its
  lock, and produces a *consistent* value that matches the earlier read
  (no anomaly), or
- outlives the 5-second timeout and SQL Server returns error 1222,
  "Lock request time out period exceeded."

Both are captured, both are treated as evidence, and both actually occurred
across the runs behind this submission (see `submission.md` for exactly
which happened where) — a timeout is not a broken test, it is SQL Server
enforcing the isolation guarantee by making the blocked statement wait
rather than let it read or write something the current isolation level
forbids.

One implementation detail mattered here: `SET LOCK_TIMEOUT` has to live in
the *same batch* as the statement it is meant to guard. Testing directly
against the container showed that when a lock-timeout error (1222) occurs
in a batch, and `LOCK_TIMEOUT` had been set in a separate, earlier batch,
sqlcmd drops every statement after the error in that batch — including the
`PRINT` marker this orchestrator depends on to know the step finished. Set
in the same batch as the statement that might time out, the batch
continues normally afterward. The six numbered scripts in `sql/` reflect
this: `SET LOCK_TIMEOUT 5000;` is always the first line of the batch that
contains the potentially-blocking statement, never a batch by itself.

## Database and schema

Database `IsolationLab`, one table: `dbo.Accounts(Id int, AccountName
nvarchar(50), Balance decimal(12,2), Category varchar(20))`. Ten seed rows,
obviously synthetic (`'Account 0001'` etc., no real names or account
numbers) — this task is about locking behaviour, not data volume, so a
handful of rows is all that's needed. `Balance` and `Category` double as
the range predicates for the phantom-read query.

## Rosetta-emulation caveat

`mcr.microsoft.com/mssql/server:2022-latest` is an `amd64`-only image.
Docker Desktop's Rosetta emulation is what lets it run at all on Apple
Silicon. This is not a Microsoft-supported configuration, but the locking
and isolation-level semantics being demonstrated are engine behaviour, not
CPU-architecture behaviour — the demonstrations are genuine, just running
somewhat slower than on native hardware.

## Layout

```
sql/
  00_create_database.sql          create IsolationLab if absent
  01_schema.sql                   create dbo.Accounts
  02_seed.sql                     (re-)seed the 10 synthetic rows
  03_verify_snapshot_off.sql      record READ_COMMITTED_SNAPSHOT / ALLOW_SNAPSHOT_ISOLATION
  10_dirty_read_sessionA.sql      writer: update, leave uncommitted, then rollback
  10_dirty_read_sessionB.sql      reader: dirty-read attempt, then post-rollback read
  11_nonrepeatable_sessionA.sql   writer: update + commit
  11_nonrepeatable_sessionB.sql   reader: read, read again, commit
  12_phantom_sessionA.sql         writer: insert a row inside the range + commit
  12_phantom_sessionB.sql         reader: range query, range query again, commit
  90_teardown.sql                 drop IsolationLab
scripts/
  run-experiment.sh               the orchestrator described above
output/                           captured transcripts and rendered scripts (see below)
tests/Day9Task1.Verification/     offline xunit checks against the files in sql/ and output/
```

Each `sessionB` file has `__ISOLATION_LEVEL__` as a placeholder, substituted
at run time — READ UNCOMMITTED vs READ COMMITTED for the dirty read, READ
COMMITTED vs REPEATABLE READ for the non-repeatable read, REPEATABLE READ
vs SERIALIZABLE for the phantom read. `sessionA` never changes: it doesn't
need to, since only the *reader's* isolation level determines whether it
sees the anomaly, and this also means the two runs of each `sessionA` file
are always byte-identical. The orchestrator renders both files per run into
`output/<anomaly>/<tag>/session{A,B}.rendered.sql` before executing them, so
what's captured is exactly what actually ran, not a reconstruction.

## Re-running it

```
./scripts/run-experiment.sh
```

Requires Docker Desktop running with the `mcr.microsoft.com/mssql/server:2022-latest`
image available and `--platform linux/amd64` support (Rosetta on Apple
Silicon). The script is self-contained: it generates its own SA password
at runtime (kept only in a shell variable, never printed, logged, or
written to any file), starts a container named `day9-sql` on a free host
port (reported in `output/run.log`), waits for it to genuinely accept
connections, runs all six scenarios with the database reset between each,
and stops and removes the container when done. Every run starts a fresh
container — the SA password is never persisted, so a stale `day9-sql` from
a previous invocation is removed and recreated rather than reused.

The script is plain bash written against bash 3.2 (macOS's default
`/bin/bash`) — no associative arrays, no `${var,,}`, no `{fd}>` dynamic file
descriptor allocation — and includes a portable wall-clock guard (macOS has
no `timeout` command by default) that force-kills the whole run if it ever
wedges.
