#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQL_DIR="$ROOT/sql"
OUTPUT_DIR="$ROOT/output"
CONTAINER="day8-covering-sql"
HOST_PORT="1434"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# --- 1. Generate a strong random SA password for this run only. -----------
# Held in this shell variable only, for the lifetime of this script. Never
# printed, never written to a file, never used in a filename, never logged.
RAW="$(openssl rand -base64 32 | tr -dc 'A-Za-z0-9')"
SA_PASSWORD="${RAW:0:24}Aa9!"

# --- 2. Always recreate the container fresh. -------------------------------
# SQL Server bakes the SA password into master at first boot, so "reusing" a
# stale container from an earlier run would hold a password this run no
# longer knows. Port 1434 is used (not 1433) so this never collides with
# Task 1's day8-sql container if it happens to still be running.
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" --platform linux/amd64 \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD="$SA_PASSWORD" \
  -e MSSQL_PID=Developer \
  -p "${HOST_PORT}:1433" \
  mcr.microsoft.com/mssql/server:2022-latest >/dev/null

sqlcmd_exec() {
  docker exec -i "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -y 0 "$@"
}

echo "Waiting for SQL Server to accept connections (host port ${HOST_PORT})..."
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

# --- 3. Extract the single query out of 03_query.sql. ----------------------
# 03_query.sql is the single source of truth; nothing here re-types it.
extract_query() {
  local file="$1"
  local buf=""
  local result=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    if [[ "$line" == "GO" ]]; then
      local trimmed
      trimmed="$(echo "$buf" | tr -d '[:space:]')"
      if [[ -n "$trimmed" ]] && [[ ! "$buf" =~ ^[[:space:]]*USE ]]; then
        result="$buf"
      fi
      buf=""
      continue
    fi
    if [[ "$line" =~ ^[[:space:]]*-- ]] || [[ -z "${line// /}" ]]; then
      continue
    fi
    buf+="$line"$'\n'
  done < "$file"
  echo -n "$result"
}
QUERY="$(extract_query "$SQL_DIR/03_query.sql")"
if [[ -z "$QUERY" ]]; then
  echo "ERROR: could not extract the query from 03_query.sql" >&2
  exit 1
fi

capture_stage() {
  local stage="$1"
  local outdir="$OUTPUT_DIR/$stage"
  mkdir -p "$outdir"

  # IO + TIME + PROFILE: plain text, includes the "logical reads" line and
  # the row-by-row actual-execution profile.
  printf 'SET NOCOUNT ON;\nSET STATISTICS IO ON;\nSET STATISTICS TIME ON;\nSET STATISTICS PROFILE ON;\n%s\n' "$QUERY" \
    | sqlcmd_exec -d CoveringLab > "$outdir/query_stats_profile.txt" 2>&1 || true

  # Actual XML plan: SET STATISTICS XML ON returns the plan as one XML
  # document per statement. sqlcmd also prints the query's own result rows
  # afterward, so we extract just the well-formed <ShowPlanXML>...</ShowPlanXML>
  # fragment rather than saving the raw sqlcmd transcript.
  printf 'SET NOCOUNT ON;\nSET STATISTICS XML ON;\n%s\n' "$QUERY" \
    | sqlcmd_exec -d CoveringLab 2>&1 \
    | sed -n '/<ShowPlanXML/,/<\/ShowPlanXML>/p' \
    > "$outdir/query_plan.sqlplan" || true
}

# --- 4. Run the staged experiment. -----------------------------------------
echo "=== 00 create database ==="
run_sql_file "$SQL_DIR/00_create_database.sql"

echo "=== 01 schema (clustered index is part of the starting state) ==="
run_sql_file "$SQL_DIR/01_schema.sql"

echo "=== 02 generate data (~100k rows) ==="
run_sql_file "$SQL_DIR/02_generate_data.sql" | tee "$OUTPUT_DIR/rowcount_after_load.txt"

echo "=== 10 non-covering index (stage1-before) ==="
run_sql_file "$SQL_DIR/10_noncovering_index.sql"

echo "=== stage1-before capture ==="
capture_stage "stage1-before"

echo "=== 11 covering index via DROP_EXISTING (stage2-after) ==="
run_sql_file "$SQL_DIR/11_covering_index.sql"

echo "=== stage2-after capture ==="
capture_stage "stage2-after"

echo "Experiment complete. Output in $OUTPUT_DIR"

# --- 5. Stop and remove the container. --------------------------------------
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
echo "Container '${CONTAINER}' stopped and removed."
