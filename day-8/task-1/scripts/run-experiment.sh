#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQL_DIR="$ROOT/sql"
OUTPUT_DIR="$ROOT/output"
CONTAINER="day8-sql"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# --- 1. Generate a strong random SA password for this run only. -----------
# Held in this shell variable only, for the lifetime of this script. Never
# printed, never written to a file, never used in a filename, never logged.
RAW="$(openssl rand -base64 32 | tr -dc 'A-Za-z0-9')"
SA_PASSWORD="${RAW:0:24}Aa9!"

# --- 2. Always recreate the container fresh. -------------------------------
# SQL Server bakes the SA password into master at first boot. A stale
# container from an earlier run would hold a DIFFERENT password than the one
# just generated above, and "reusing" it would silently break authentication.
# Removing and recreating keeps this script re-runnable from a clean state
# with no manual intervention, at the cost of always re-seeding data.
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" --platform linux/amd64 \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD="$SA_PASSWORD" \
  -e MSSQL_PID=Developer \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null

sqlcmd_exec() {
  docker exec -i "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -y 0 "$@"
}

echo "Waiting for SQL Server to accept connections..."
ready=0
for _ in $(seq 1 60); do
  if sqlcmd_exec -Q "SELECT 1" >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" -ne 1 ]]; then
  echo "ERROR: SQL Server did not become ready in time." >&2
  exit 1
fi
echo "SQL Server is ready."

run_sql_file() {
  local file="$1"
  # sqlcmd runs inside the container via `docker exec`, so a host path is not
  # valid for -i; pipe the file's contents over stdin instead.
  sqlcmd_exec < "$file"
}

# --- 3. Extract Q1/Q2/Q3 text out of 03_queries.sql. -----------------------
# 03_queries.sql is the single source of truth; nothing here re-types the
# query bodies. Batches are separated by lines that are exactly "GO";
# comment lines (--) are stripped; the leading "USE IndexLab;" batch is
# skipped so only the three SELECT batches remain.
declare -a QUERIES
extract_queries() {
  local file="$1"
  QUERIES=()
  local buf=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "GO" ]]; then
      local trimmed
      trimmed="$(echo "$buf" | tr -d '[:space:]')"
      if [[ -n "$trimmed" ]] && [[ ! "$buf" =~ ^[[:space:]]*USE ]]; then
        QUERIES+=("$buf")
      fi
      buf=""
      continue
    fi
    if [[ "$line" =~ ^[[:space:]]*-- ]] || [[ -z "${line// /}" ]]; then
      continue
    fi
    buf+="$line"$'\n'
  done < "$file"
}
extract_queries "$SQL_DIR/03_queries.sql"
if [[ "${#QUERIES[@]}" -ne 3 ]]; then
  echo "ERROR: expected 3 queries extracted from 03_queries.sql, got ${#QUERIES[@]}" >&2
  exit 1
fi

# --- 4. Per-stage, per-query capture helpers. -------------------------------
capture_query() {
  local stage="$1" qnum="$2" query="$3"
  local outdir="$OUTPUT_DIR/$stage"
  mkdir -p "$outdir"

  # IO + TIME + PROFILE: plain text, includes the "logical reads" lines and
  # the row-by-row actual-execution profile.
  printf 'SET NOCOUNT ON;\nSET STATISTICS IO ON;\nSET STATISTICS TIME ON;\nSET STATISTICS PROFILE ON;\n%s\n' "$query" \
    | sqlcmd_exec -d IndexLab > "$outdir/q${qnum}_stats_profile.txt" 2>&1 || true

  # Actual XML plan: SET STATISTICS XML ON returns the plan as one XML
  # document per statement. sqlcmd also prints the query's own result rows
  # afterward, so we extract just the well-formed <ShowPlanXML>...</ShowPlanXML>
  # fragment rather than saving the raw sqlcmd transcript.
  printf 'SET NOCOUNT ON;\nSET STATISTICS XML ON;\n%s\n' "$query" \
    | sqlcmd_exec -d IndexLab 2>&1 \
    | sed -n '/<ShowPlanXML/,/<\/ShowPlanXML>/p' \
    > "$outdir/q${qnum}_plan.sqlplan" || true
}

capture_stage() {
  local stage="$1"
  for i in 1 2 3; do
    capture_query "$stage" "$i" "${QUERIES[$((i-1))]}"
  done
}

capture_writecost() {
  local stage="$1"
  local outdir="$OUTPUT_DIR/$stage"
  mkdir -p "$outdir"
  run_sql_file "$SQL_DIR/20_write_cost_insert.sql" > "$outdir/insert_stats.txt" 2>&1
}

# --- 5. Run the staged experiment. -----------------------------------------
echo "=== 00 create database ==="
run_sql_file "$SQL_DIR/00_create_database.sql"

echo "=== 01 schema heap (stage0) ==="
run_sql_file "$SQL_DIR/01_schema_heap.sql"

echo "=== 02 generate data (~100k rows) ==="
run_sql_file "$SQL_DIR/02_generate_data.sql" | tee "$OUTPUT_DIR/rowcount_after_load.txt"

echo "=== stage0-heap captures ==="
capture_stage "stage0-heap"

echo "=== 10 clustered index ==="
run_sql_file "$SQL_DIR/10_clustered_index.sql"

echo "=== stage1-clustered captures ==="
capture_stage "stage1-clustered"

echo "=== writecost-clustered-only ==="
capture_writecost "writecost-clustered-only"

echo "=== 11 nonclustered customer index ==="
run_sql_file "$SQL_DIR/11_nonclustered_customer.sql"

echo "=== stage2-nc-customer captures ==="
capture_stage "stage2-nc-customer"

echo "=== 12 nonclustered covering index ==="
run_sql_file "$SQL_DIR/12_nonclustered_covering.sql"

echo "=== stage3-nc-covering captures ==="
capture_stage "stage3-nc-covering"

echo "=== writecost-all-indexes ==="
capture_writecost "writecost-all-indexes"

echo "Experiment complete. Output in $OUTPUT_DIR"
