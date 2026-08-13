# Day 4 — Task 1: Wire CI with GitHub Actions

## What this is

A root-level GitHub Actions workflow (`.github/workflows/ci.yml`) that builds, tests, and
enforces a 70% line-coverage gate on every push to any branch and on every pull request into
`main`. This folder holds the documentation and a small, self-contained sample project the
workflow builds against.

The active workflow file lives at the repo root — GitHub Actions only reads workflows from
`.github/workflows/` at the repository root, so it cannot live inside this task folder.
[`workflow-reference.yml`](./workflow-reference.yml) in this folder is a read-only copy of that
same file, kept here purely so a reviewer can read it alongside this README without leaving the
task folder. It is **not** executed — only the root copy runs. Keep the two in sync manually.

## Scope decision: what does CI actually build?

`day-4/task-1` started out empty and the repository has no root-level `.sln`/`.slnx` — every
existing project (`day-1` through `day-3`) has its own solution scoped to its own task folder,
and none of their test projects reference `coverlet.collector` (a prerequisite for
`--collect:"XPlat Code Coverage"` to produce any output). Wiring CI meaningfully required picking
a build target, and three options were considered:

1. **A new, self-contained sample project inside `day-4/task-1`** (chosen) — a tiny class library
   (`CiDemo`) plus an xunit test project (`CiDemo.Tests`, with `coverlet.collector` added), with
   the workflow's `dotnet` commands pointed at `day-4/task-1/Task1.slnx`. Everything needed lives
   inside this task folder plus the one root workflow file — no other day/task folder is touched,
   and the coverage number is genuine and easy to audit.
2. Point CI at `day-3/task-7`'s real Quotes API and its SQL Server/Testcontainers integration
   tests — would reuse real app code, but requires editing a file outside this task folder and
   makes every push on every branch spin up a Docker/SQL Server container, which is heavy for a
   generic "any branch" gate.
3. Build/test the entire repository — would require adding `coverlet.collector` to all ~13
   existing test projects (files outside this task folder) and risks the 70% gate failing for
   reasons that have nothing to do with this task.

Option 1 was chosen to keep the change self-contained and the coverage signal honest. In a real
project, the equivalent choice would be "the repo's actual solution/project," not a purpose-built
sample — this workflow's `SOLUTION_PATH` environment variable is the one line to change to retarget
it.

## Workflow walkthrough (`.github/workflows/ci.yml`)

- **Triggers**: `push` with no branch filter (runs on every branch) and `pull_request` targeting
  `main` only.
- **Permissions**: `contents: read` only — the job doesn't need to write to the repo.
- **Concurrency**: grouped by `github.ref` with `cancel-in-progress: true`, so pushing again to
  the same branch cancels the in-flight run instead of piling up runs.
- **Steps**, in the order the exercise specifies:
  1. `actions/checkout@v5`
  2. `actions/setup-dotnet@v5` installing the `10.0.x` SDK
  3. `dotnet restore`
  4. `dotnet build --no-restore`
  5. `dotnet test --no-build` with the TRX logger and the `XPlat Code Coverage` collector, writing
     into a predictable `--results-directory` (`test-results/`) rather than a random GUID folder
- **Artifacts**: the TRX file and the Cobertura coverage file are uploaded as separate artifacts,
  both with `if: always()` — so a **red** run still publishes diagnostics instead of leaving the
  reviewer with nothing to look at.
- **Coverage gate**: a final step (`if: always()`, so the percentage is always printed even after
  a test failure) runs [`scripts/check_coverage.py`](./scripts/check_coverage.py) against the
  produced `coverage.cobertura.xml` and the `COVERAGE_THRESHOLD` (`70`) env var.

### Action version pins

Pinned to `actions/checkout@v5`, `actions/setup-dotnet@v5`, and `actions/upload-artifact@v4` as
specified for this exercise. Newer majors exist as of August 2026 (`checkout@v6`/`v7`,
`setup-dotnet@v6`, `upload-artifact@v6`/`v7` — the repo's existing per-task workflows already use
`checkout@v7`/`setup-dotnet@v6`). This workflow pins one major back from those to match the
versions explicitly called for in this task; there is nothing wrong with the newer majors, this
is a deliberate, conservative choice for this exercise rather than a compatibility requirement.

### How the coverage gate works

`dotnet test --collect:"XPlat Code Coverage"` invokes the `coverlet.collector` package (referenced
by `CiDemo.Tests.csproj`) and writes a Cobertura-format XML report. `scripts/check_coverage.py`:

1. Globs `**/coverage.cobertura.xml` under the results directory.
2. Deduplicates reports by content hash — locally, `dotnet test` on this SDK was observed to write
   a byte-identical second copy of the same coverage report into an extra
   `_<machine>_<timestamp>/In/<machine>/` folder alongside the canonical
   `<results-dir>/<guid>/coverage.cobertura.xml`. Without deduping, summing both would double-count
   the same measurement (harmless when the ratio is unchanged, but incorrect in principle and
   unsafe to rely on for multi-project solutions).
3. Sums each remaining report's `lines-covered` / `lines-valid` attributes (a proper weighted
   average across projects, not an average of percentages).
4. Prints the measured percentage and the required threshold, then exits non-zero if measured
   coverage is below the threshold — including when no coverage report exists at all, so a broken
   collector fails loudly instead of silently passing.

The gate is never weakened with `|| true` or `continue-on-error`, and the threshold is never
rounded in its own favor.

## Branch protection: requiring this check on `main`

A status check can only be selected as **required** after it has run at least once on the repo.
Order of operations: push this workflow → let it run once (via this branch or a PR) → then
configure the rule below.

This also requires **admin** rights on `thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal`,
which the submitting student may not have. Check your permission level first:

```
gh api repos/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal --jq '.permissions'
```

(or, in the browser: open the repo → Settings — if you don't see a "Settings" tab, or the
Branches/Rules pages 404 or show "you do not have access," you are not an admin and must ask your
mentor to add the rule instead.)

### Click-path (classic branch protection rule)

1. Repo → **Settings** → **Branches**.
2. Under "Branch protection rules," click **Add branch protection rule** (or edit the existing
   rule for `main` if one exists).
3. Branch name pattern: `main`.
4. Enable **Require status checks to pass before merging**.
5. Enable **Require branches to be up to date before merging** (recommended, keeps the check
   honest against the latest `main`).
6. In the status checks search box, select the job name from this workflow (the check reports as
   the job name, `build-test`) — it will only appear in the list after the workflow has run once.
7. Save changes.

GitHub also offers the newer **Rulesets** (Settings → Rules → Rulesets) as an alternative to
classic branch protection rules, with the same "require status checks" capability plus finer
targeting (e.g. multiple branch patterns, bypass lists). Either works for this exercise; classic
rules are documented step-by-step above because they map most directly to the task's wording.
Note that on private repositories some protection/ruleset features are gated by GitHub plan —
if an option is missing, that's the likely reason, not a misconfiguration.

**Safe to screenshot/share**: the rule's settings screen itself (branch name pattern, which
checkboxes are enabled, which status check is selected) — this is configuration, not secret data.
**Keep private**: anything under Settings that shows collaborator lists, tokens, or webhook
secrets — stay on the Branches/Rules screens only when sharing a screenshot.

## Local verification performed

Run from the repo root:

```
dotnet restore day-4/task-1/Task1.slnx
dotnet build day-4/task-1/Task1.slnx --no-restore
dotnet test day-4/task-1/Task1.slnx --no-build \
  --logger "trx;LogFileName=test-results.trx" \
  --collect:"XPlat Code Coverage" \
  --results-directory <results-dir>
python3 day-4/task-1/scripts/check_coverage.py <results-dir> 70
```

Result: build succeeded, **12/12 tests passed**, measured line coverage **100.00%** (18/18 lines)
against the 70% threshold — gate passed. The gate script was also exercised against an impossible
100.01% threshold (correctly failed, exit 1) and a missing results directory (correctly failed
with "No coverage.cobertura.xml found," exit 1) to confirm it doesn't silently pass on a broken
collector.

`ci.yml` was validated as parseable YAML using Ruby's built-in Psych library
(`ruby -ryaml -e "YAML.load_file('.github/workflows/ci.yml')"`) — PyYAML wasn't installed locally
and installing it wasn't necessary given the built-in alternative. This caught and fixed one real
issue: the bare `on:` key parses as the boolean `true` under standard YAML 1.1 (GitHub's own
parser special-cases it, but generic tooling won't), so the key is quoted as `"on":` in both
`ci.yml` and this folder's reference copy.
