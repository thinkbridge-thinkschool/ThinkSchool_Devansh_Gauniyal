## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-9/task-1/day-9/task-1

## Notes for mentor

### Resolved ambiguities (please challenge any of these)

1. **Engine.** The task names ANSI isolation levels, but this follows Day 8's SQL Server work, so I demonstrated them on SQL Server 2022 Developer edition in Docker, forced to `--platform linux/amd64` under Docker Desktop's Rosetta emulation on Apple Silicon. This isn't a Microsoft-supported configuration, but locking/isolation semantics are engine behaviour, not CPU behaviour, so the demonstrations are genuine.
2. **Schema.** No table was named, so I used one database (`IsolationLab`) with one table, `dbo.Accounts(Id int, AccountName nvarchar(50), Balance decimal(12,2), Category varchar(20))`, seeded with 10 obviously-synthetic rows (`'Account 0001'`, etc). This is about locking behaviour, not volume, so a small synthetic table is the right scope.
3. **Snapshot settings.** Before running anything, I queried `sys.databases` for `IsolationLab` and recorded `READ_COMMITTED_SNAPSHOT = 0` (OFF) and `ALLOW_SNAPSHOT_ISOLATION state = OFF` (captured in `output/00_snapshot_settings.txt`). Both were OFF as expected, so READ COMMITTED uses shared locks, not row versioning, and the non-repeatable-read demonstration behaves as the classic locking model predicts.
4. **"Show which level prevents each" methodology.** For each anomaly I ran the *same* two-session script twice: once at a level where the anomaly occurs, once at the lowest level that prevents it — six captured runs total, all in `output/`.
5. **What prevention looks like.** Every session sets `SET LOCK_TIMEOUT 5000` (5 seconds) so a blocked statement fails loudly with error 1222 instead of hanging. Depending on the exact run, prevention showed up either as a consistent (non-differing) second read or as a genuine 1222 timeout on the blocked statement — both are treated as valid proof, and which one happened on each real run is reported honestly below, not assumed.
6. **The expected answer table.** dirty read → READ COMMITTED, non-repeatable read → REPEATABLE READ, phantom read → SERIALIZABLE. Every row below is backed by real captured output, not asserted from memory.

### Methodology: why two fire-and-forget sends needed a synchronization fix

The two sessions are driven from one bash orchestrator (`scripts/run-experiment.sh`) through named pipes (`mkfifo`), not `sleep`/`WAITFOR` — the orchestrator writes each statement to a session's stdin only once it has confirmed (via a `PRINT 'MARK:...'` appended to the previous statement, polled for in that session's transcript) that the prior dependent step actually executed. Testing this directly against the container surfaced one real bug: sending Session B's dirty-read attempt (which might block, so it can't be waited on) immediately followed by Session A's rollback let A's rollback occasionally reach the server before B's read was even dispatched, since the two run on independent connections. The fix, described in `README.md`, is a marker printed *before* the read as well as after, so the orchestrator can confirm the read has genuinely started before releasing the other session. I'm noting this because it's the kind of race that "looks correct" until you actually run it enough times to see it fail — which is exactly why the task insisted on determinism instead of timing.

### Anomaly 1 — dirty read

**Session A** (`sql/10_dirty_read_sessionA.sql`) — the writer:
```sql
-- STEP 1: begin a transaction and update Account 2's balance, do not commit.
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
UPDATE dbo.Accounts SET Balance = 9999.99 WHERE Id = 2;
GO

-- STEP 2: roll back — the update above must never be committed.
ROLLBACK TRANSACTION;
GO
```

**Session B** (`sql/10_dirty_read_sessionB.sql`) — the reader:
```sql
-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL __ISOLATION_LEVEL__;
GO

-- STEP 2: read while Session A's update is uncommitted (the dirty-read attempt).
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 2;
GO

-- STEP 3: read again after Session A has rolled back.
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 2;
GO
```

Interleaving: **A1** (begin+update, uncommitted) → **B1** (set isolation) → **B2** (dirty-read attempt) → **A2** (rollback) → **B3** (post-rollback read). `__ISOLATION_LEVEL__` is the only thing that changes between the two runs below.

**Occurs — READ UNCOMMITTED** (`output/10_dirty_read/occurs_READ_UNCOMMITTED/sessionB.transcript.txt`):
```
Id          Balance
-----------  --------------
          2        9999.99      <- dirty read: A's uncommitted value
(1 rows affected)

Id          Balance
-----------  --------------
          2        1500.00      <- post-rollback: the real, original value
(1 rows affected)
```
The reader saw 9999.99, a value that was rolled back and never really existed. SPIDs: A=51, B=54 (two distinct sessions, confirmed in `spids.txt`).

**Prevented — READ COMMITTED** (`output/10_dirty_read/prevented_READ_COMMITTED/sessionB.transcript.txt`):
```
Msg 1222, Level 16, State 51, Server ..., Line 5
Lock request time out period exceeded.        <- B's read blocked on A's exclusive lock and timed out

Id          Balance
-----------  --------------
          2        1500.00      <- post-rollback read
(1 rows affected)
```
B's read never observed 9999.99 — it either waits for A's lock and gets the real value, or, as happened in this actual run, times out at 5 seconds before A's rollback (sent immediately after) gets a chance to unblock it. Either outcome is proof the dirty read did not get through.

### Anomaly 2 — non-repeatable read

**Session A** (`sql/11_nonrepeatable_sessionA.sql`) — the writer:
```sql
-- STEP 1: update Account 1's balance. No explicit transaction - auto-commits
-- on success. Under REPEATABLE READ this blocks until Session B's
-- transaction ends (commit) or this statement's own LOCK_TIMEOUT expires
-- first (error 1222).
SET LOCK_TIMEOUT 5000;
UPDATE dbo.Accounts SET Balance = 1111.11 WHERE Id = 1;
GO
```

**Session B** (`sql/11_nonrepeatable_sessionB.sql`) — the reader:
```sql
-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL __ISOLATION_LEVEL__;
GO

-- STEP 2: begin a transaction and read Account 1 (first read).
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 1;
GO

-- STEP 3: read Account 1 again, inside the same transaction (second read).
SELECT Id, Balance FROM dbo.Accounts WHERE Id = 1;
GO

-- STEP 4: commit, releasing any lock this transaction is holding.
COMMIT TRANSACTION;
GO
```

Interleaving: **B1** (set isolation) → **B2** (begin+first read) → **A1** (update+commit) → **B3** (second read) → **B4** (commit).

**Occurs — READ COMMITTED** (`output/11_nonrepeatable/occurs_READ_COMMITTED/sessionB.transcript.txt`): first read `1000.00`, second read `1111.11` — the two reads of the same row, in the same transaction, differ.

**Prevented — REPEATABLE READ** (`output/11_nonrepeatable/prevented_REPEATABLE_READ/`): Session B's first and second read both return `1000.00` — identical. Session A's transcript shows the real cause: `Msg 1222, Level 16, State 51 ... Lock request time out period exceeded. The statement has been terminated.` — A's update genuinely blocked on B's shared lock and timed out before B committed.

### Anomaly 3 — phantom read

**Session A** (`sql/12_phantom_sessionA.sql`) — the writer:
```sql
-- STEP 1: insert a new account whose balance (2750.00) falls inside the
-- range Session B is querying (2000.00 to 3000.00).
SET LOCK_TIMEOUT 5000;
INSERT INTO dbo.Accounts (Id, AccountName, Balance, Category)
VALUES (11, N'Account 0011', 2750.00, 'Retail');
GO
```

**Session B** (`sql/12_phantom_sessionB.sql`) — the reader:
```sql
-- STEP 1: set the isolation level under test.
SET LOCK_TIMEOUT 5000;
SET TRANSACTION ISOLATION LEVEL __ISOLATION_LEVEL__;
GO

-- STEP 2: begin a transaction and run the range query (first read).
BEGIN TRANSACTION;
SELECT Id, Balance FROM dbo.Accounts WHERE Balance BETWEEN 2000.00 AND 3000.00 ORDER BY Id;
GO

-- STEP 3: re-run the same range query, inside the same transaction (second read).
SELECT Id, Balance FROM dbo.Accounts WHERE Balance BETWEEN 2000.00 AND 3000.00 ORDER BY Id;
GO

-- STEP 4: commit.
COMMIT TRANSACTION;
GO
```

The phantom is produced by an **INSERT** (never an UPDATE) — the whole point of this anomaly is that a row lock cannot protect a range that doesn't have a row in it yet.

**Occurs — REPEATABLE READ** (`output/12_phantom/occurs_REPEATABLE_READ/sessionB.transcript.txt`): first range read returns 3 rows (Ids 3, 4, 5); second range read returns 4 rows (Ids 3, 4, 5, **11**) — Account 11 is the phantom. REPEATABLE READ locks the three rows already read but not the gap they sit in, so A's insert goes through and commits.

**Prevented — SERIALIZABLE** (`output/12_phantom/prevented_SERIALIZABLE/`): both range reads return the same 3 rows. Session A's transcript shows `Msg 1222 ... Lock request time out period exceeded. The statement has been terminated.` — SERIALIZABLE locks the *range* B queried, so A's insert into that range blocked and timed out.

### The table

| Anomaly | Lowest isolation level that prevents it | Evidence |
|---|---|---|
| Dirty read | READ COMMITTED | `output/10_dirty_read/prevented_READ_COMMITTED/` — reader never sees 9999.99; blocks then reads 1500.00, or times out (1222) |
| Non-repeatable read | REPEATABLE READ | `output/11_nonrepeatable/prevented_REPEATABLE_READ/` — reader's two reads both 1000.00; writer times out (1222) |
| Phantom read | SERIALIZABLE | `output/12_phantom/prevented_SERIALIZABLE/` — reader's two range reads both return 3 rows; writer times out (1222) |

REPEATABLE READ stops the non-repeatable read but not the phantom, because it only holds shared locks on rows it has actually read — it has nothing to lock for a row that doesn't exist yet. SERIALIZABLE closes that gap by locking the *range itself* (a key-range lock), so nothing can be inserted into or deleted from the queried range until the transaction ends. That's the actual conceptual difference this exercise is testing, not just "a stricter setting."

## What did you learn this session?

I learned that REPEATABLE READ and SERIALIZABLE aren't just "stricter levels" on a line — they lock fundamentally different things (rows already read vs. the range itself), which is why one stops non-repeatable reads and the other is needed for phantoms.

## What would break this?

Two fire-and-forget sends in a row race unless you checkpoint that the first one actually started — I hit that bug directly (documented above). Turning on READ_COMMITTED_SNAPSHOT would also break the non-repeatable-read demo, since READ COMMITTED would then use row versioning instead of the shared locks this relies on.
