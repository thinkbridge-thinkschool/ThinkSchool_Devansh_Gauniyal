## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-9/task-2/day-9/task-2

## Notes for mentor

### Resolved ambiguities (please challenge any of these)

1. **Engine.** Deadlock graphs, Extended Events, trace flag 1222, and the deadlock monitor are SQL Server features. I ran SQL Server 2022 Developer edition in Docker forced to `--platform linux/amd64` under Docker Desktop's Rosetta emulation on Apple Silicon — not officially supported by Microsoft for production, but a widely used development configuration. Lock management and deadlock detection are engine behaviour, not CPU behaviour, so the demonstration is genuine.
2. **Schema.** The task names no tables; "two-resource" is the operative phrase. I used database `DeadlockLab` with two separate small tables, `dbo.Accounts` and `dbo.Orders`, each holding a couple of obviously-synthetic rows (`Account 0001`, `Synthetic order row`). Two distinct tables make the resource-acquisition order explicit and readable, which is the point of the exercise; small row counts are correct here since this is about lock ordering, not volume.
3. **"Classic two-resource deadlock."** Session A updates Accounts row Id=1, then requests Orders row Id=1. Session B updates Orders row Id=1 first, then requests Accounts row Id=1. The orchestrator fires both requesting statements without waiting on either, so a genuine circular wait forms: A holds Accounts and waits on Orders; B holds Orders and waits on Accounts.
4. **"Fix by consistent lock ordering."** The fixed scripts change nothing except the order in which the two tables are touched — both sessions now lock Accounts before Orders. No added hints, no isolation-level change, no added indexes, no retry loop, no `SET DEADLOCK_PRIORITY`. The verification test suite parses and enforces this directly (`Fixed_session_B_differs_from_broken_session_B_only_in_statement_order`).
5. **Capture method.** I did both, since each is a fallback for the other: (a) Extended Events — queried the default `system_health` session's ring buffer for the `xml_deadlock_report` event and saved it as `.xdl`; (b) trace flag 1222 — enabled it, reproduced the deadlock, read the report out of the error log via `xp_readerrorlog`, then disabled the flag and confirmed it was off. Both captures below are real output from the same run; the trace-flag route turned out to render the report as indented plain text, not angle-bracket XML — noted where it matters.
6. **Victim is non-deterministic.** SQL Server chooses the victim by estimated rollback cost. The tests assert exactly one of the two sessions reports error 1205, without hardcoding which. In the run captured below, **Session A (SPID 54) was the victim**; a re-run could pick Session B instead.

### Number disambiguation

- **Trace flag 1222** — a diagnostic flag (`DBCC TRACEON (1222, -1)`) that writes a deadlock report to the SQL Server error log.
- **Error 1205** — "Transaction was deadlocked on lock resources... chosen as the deadlock victim." This is the signal a real deadlock occurred.
- **Error 1222** — "Lock request time out period exceeded." A lock *timeout*, unrelated to trace flag 1222 despite the shared number, and **not** a deadlock. Neither transcript below contains it.

### The repro scripts, verbatim

Global interleaving (both fired by `scripts/run-experiment.sh` over named pipes, one session at a time, matching this order):

1. **Session A step 1** — lock Accounts row Id=1 (commits nothing yet)
2. **Session B step 1** — lock Orders row Id=1 (commits nothing yet)
3. **Session A step 2** and **Session B step 2** — both fired *without waiting on either* (see README.md for why waiting on the first would hang the orchestrator forever): A requests Orders (B holds it), B requests Accounts (A holds it) — a circular wait
4. Both sessions' step 3 — commit attempt (harmless error if that session was the victim)

`sql/10_deadlock_sessionA.sql`:
```sql
BEGIN TRANSACTION;

-- STEP 1: lock Accounts row Id=1.
UPDATE dbo.Accounts SET Balance = Balance + 100.00 WHERE Id = 1;
GO

-- STEP 2: request Orders row Id=1 — Session B holds this lock at this
-- point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-A' WHERE Id = 1;
GO

-- STEP 3: commit if this session survived. If this session was chosen as
-- the deadlock victim, SQL Server already rolled its transaction back, and
-- this COMMIT errors ("no corresponding BEGIN TRANSACTION") — that harmless
-- secondary error is itself part of the captured evidence, not a bug.
COMMIT TRANSACTION;
GO
```

`sql/11_deadlock_sessionB.sql` (the BROKEN ordering — reverse of A):
```sql
BEGIN TRANSACTION;

-- STEP 1: lock Orders row Id=1.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-B' WHERE Id = 1;
GO

-- STEP 2: request Accounts row Id=1 — Session A holds this lock at this
-- point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
GO

-- STEP 3: commit if this session survived (see Session A's file for why a
-- harmless secondary error here is expected for the victim).
COMMIT TRANSACTION;
GO
```

### The deadlock graph and victim message (real captured output from one run)

**Session A's transcript** (`output/10_deadlock_broken/sessionA.transcript.txt`) — Session A was the victim:
```
SPID:54
MARK:SPID_A_CAPTURED

(1 rows affected)
MARK:A_STEP1_LOCK_ACCOUNTS_DONE
MARK:A_STEP2_STARTING
Msg 1205, Level 13, State 51, Server 27ee1b19a82a, Line 4
Transaction (Process ID 54) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
MARK:A_STEP2_DONE
Msg 3902, Level 16, State 1, Server 27ee1b19a82a, Line 6
The COMMIT TRANSACTION request has no corresponding BEGIN TRANSACTION.
MARK:A_STEP3_COMMIT_DONE
```

**Session B's transcript** (`output/10_deadlock_broken/sessionB.transcript.txt`) — Session B survived:
```
SPID:51
MARK:SPID_B_CAPTURED

(1 rows affected)
MARK:B_STEP1_LOCK_ORDERS_DONE
MARK:B_STEP2_STARTING

(1 rows affected)
MARK:B_STEP2_DONE
MARK:B_STEP3_COMMIT_DONE
```

**Extended Events deadlock graph** (`output/deadlock_graph.xdl`, real capture, well-formed XML — trimmed here to remove the raw memory-address stack frames, which are not relevant to the lock cycle; the full untrimmed file is in `output/`):
```xml
<event name="xml_deadlock_report" package="sqlserver" timestamp="2026-08-19T09:18:38.712Z">
 <data name="xml_report"><value><deadlock>
  <victim-list><victimProcess id="processf006adc28"/></victim-list>
  <process-list>
   <process id="processf006adc28" waitresource="KEY: 5:72057594045792256 (8194443284a0)" spid="54" isolationlevel="read committed (2)" currentdbname="DeadlockLab">
    <inputbuf>
-- STEP 2: request Orders row Id=1 — Session B holds this lock at this point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-A' WHERE Id = 1;
    </inputbuf>
   </process>
   <process id="processf15f00108" waitresource="KEY: 5:72057594045726720 (8194443284a0)" spid="51" isolationlevel="read committed (2)" currentdbname="DeadlockLab">
    <inputbuf>
-- STEP 2: request Accounts row Id=1 — Session A holds this lock at this point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
    </inputbuf>
   </process>
  </process-list>
  <resource-list>
   <keylock objectname="DeadlockLab.dbo.Orders" mode="X">
    <owner-list><owner id="processf15f00108" mode="X"/></owner-list>
    <waiter-list><waiter id="processf006adc28" mode="X" requestType="wait"/></waiter-list>
   </keylock>
   <keylock objectname="DeadlockLab.dbo.Accounts" mode="X">
    <owner-list><owner id="processf006adc28" mode="X"/></owner-list>
    <waiter-list><waiter id="processf15f00108" mode="X" requestType="wait"/></waiter-list>
   </keylock>
  </resource-list>
 </deadlock></value></data>
</event>
```

**Trace flag 1222 error-log capture** (`output/errorlog_deadlock_report.txt`, real capture — note this route renders as indented plain text, not angle-bracket XML, which is genuine SQL Server behaviour for this trace flag, trimmed here to drop the stack-frame address dump):
```
2026-08-19 09:18:38.710 spid40s deadlock-list
2026-08-19 09:18:38.710 spid40s  deadlock victim=processf006adc28
2026-08-19 09:18:38.710 spid40s   process-list
2026-08-19 09:18:38.710 spid40s    process id=processf006adc28 waitresource=KEY: 5:72057594045792256 (8194443284a0) spid=54 loginname=sa isolationlevel=read committed (2) currentdbname=DeadlockLab
2026-08-19 09:18:38.740 spid40s     inputbuf
2026-08-19 09:18:38.740 spid40s
-- STEP 2: request Orders row Id=1 — Session B holds this lock at this point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-A' WHERE Id = 1;
2026-08-19 09:18:38.740 spid40s    process id=processf15f00108 waitresource=KEY: 5:72057594045726720 (8194443284a0) spid=51 loginname=sa isolationlevel=read committed (2) currentdbname=DeadlockLab
2026-08-19 09:18:38.760 spid40s     inputbuf
2026-08-19 09:18:38.760 spid40s
-- STEP 2: request Accounts row Id=1 — Session A holds this lock at this point, so this blocks until the deadlock monitor intervenes.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
2026-08-19 09:18:38.760 spid40s   resource-list
2026-08-19 09:18:38.760 spid40s    keylock objectname=DeadlockLab.dbo.Orders mode=X
2026-08-19 09:18:38.760 spid40s     owner-list
2026-08-19 09:18:38.760 spid40s      owner id=processf15f00108 mode=X
2026-08-19 09:18:38.770 spid40s     waiter-list
2026-08-19 09:18:38.770 spid40s      waiter id=processf006adc28 mode=X requestType=wait
2026-08-19 09:18:38.770 spid40s    keylock objectname=DeadlockLab.dbo.Accounts mode=X
2026-08-19 09:18:38.770 spid40s     owner-list
2026-08-19 09:18:38.770 spid40s      owner id=processf006adc28 mode=X
2026-08-19 09:18:38.770 spid40s     waiter-list
2026-08-19 09:18:38.770 spid40s      waiter id=processf15f00108 mode=X requestType=wait
```

### The fix, verbatim, with evidence

`sql/20_fixed_sessionA.sql` is byte-identical in every statement to `10_deadlock_sessionA.sql` (only the header comment differs, to describe the fixed-run context — Session A's order was never the problem).

`sql/21_fixed_sessionB.sql` (the ONLY change from `11_deadlock_sessionB.sql` is the order of the two UPDATEs):
```sql
BEGIN TRANSACTION;

-- STEP 1: request Accounts row Id=1 — this blocks until Session A commits.
UPDATE dbo.Accounts SET Balance = Balance + 50.00 WHERE Id = 1;
GO

-- STEP 2: request Orders row Id=1 — Session A has already released it by
-- the time this session gets here, so this succeeds immediately.
UPDATE dbo.Orders SET OrderStatus = 'Updated-by-B' WHERE Id = 1;
GO

-- STEP 3: commit.
COMMIT TRANSACTION;
GO
```

**Session A's transcript** (`output/20_fixed/sessionA.transcript.txt`):
```
SPID:75
MARK:SPID_A_CAPTURED

(1 rows affected)
MARK:A_STEP1_LOCK_ACCOUNTS_DONE

(1 rows affected)
MARK:A_STEP2_LOCK_ORDERS_DONE
MARK:A_STEP3_COMMIT_DONE
```

**Session B's transcript** (`output/20_fixed/sessionB.transcript.txt`) — B's first request queues behind A (ordinary blocking, not a deadlock), then completes once A commits:
```
SPID:74
MARK:SPID_B_CAPTURED
MARK:B_STEP1_STARTING

(1 rows affected)
MARK:B_STEP1_DONE

(1 rows affected)
MARK:B_STEP2_LOCK_ORDERS_DONE
MARK:B_STEP3_COMMIT_DONE
```

No 1205, no 1222, in either transcript. Both sessions' updates affected a row and both committed.

**Why consistent lock ordering works** (one line): a deadlock requires a cycle in the wait-for graph, and if every transaction acquires resources in the same global order then no transaction can ever hold a later resource while waiting on an earlier one, so no cycle can form.

## What did you learn this session?

`PRINT` output from a blocking statement doesn't reach the transcript until that statement's whole batch finishes, so a marker meant to confirm "dispatch started" has to be its own separate `GO` batch, not appended to the blocking one.

## What would break this?

Consistent ordering only holds if every code path agrees on it — one stored procedure, or one developer, touching Accounts and Orders in the reverse order reopens the exact same cycle. It also assumes the resources are known up front; if a transaction discovers which rows to lock only at runtime, there may be no fixed order to enforce at all.
