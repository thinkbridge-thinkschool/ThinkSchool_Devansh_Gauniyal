#!/usr/bin/env bash
# Builds a fresh SQLite database from sql/01_schema.sql + sql/02_seed.sql, then executes
# each query file for real and captures its actual stdout into results/*.txt, including a
# real row count. Re-run any time -- the db file is rebuilt from scratch every time.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_DIR="$SCRIPT_DIR/sql"
RESULTS_DIR="$SCRIPT_DIR/results"
DB_PATH="$SCRIPT_DIR/quotes.db"

rm -f "$DB_PATH"
mkdir -p "$RESULTS_DIR"

sqlite3 "$DB_PATH" ".read $SQL_DIR/01_schema.sql" ".read $SQL_DIR/02_seed.sql"

# $1 sql file, $2 output file -- for single-statement query files: captures the rows, then
# appends a real counted "Row count: N" line by re-running the same file wrapped in COUNT(*).
run_query_with_count() {
    local sql_file="$1"
    local out_file="$2"

    local body
    body="$(sqlite3 -header -column "$DB_PATH" "PRAGMA foreign_keys = ON;" ".read $sql_file")"

    local sql_no_semi
    sql_no_semi="$(sed -e 's/;[[:space:]]*$//' "$sql_file")"
    local row_count
    row_count="$(sqlite3 "$DB_PATH" "PRAGMA foreign_keys = ON;" "SELECT COUNT(*) FROM ($sql_no_semi);")"

    {
        echo "$body"
        echo
        echo "Row count: $row_count"
    } > "$out_file"
}

run_query_with_count "$SQL_DIR/10_q1_authors_with_quotes_no_tags.sql" "$RESULTS_DIR/10_q1_authors_with_quotes_no_tags.txt"
run_query_with_count "$SQL_DIR/11_q2_authors_in_both_sets.sql"        "$RESULTS_DIR/11_q2_authors_in_both_sets.txt"
run_query_with_count "$SQL_DIR/12_q3_combined_distinct_tags.sql"      "$RESULTS_DIR/12_q3_combined_distinct_tags.txt"

# 20_operator_contrasts.sql is several independent statements in one file, each already
# surfacing its own row count (or being a small, directly-countable result set) as part of
# what it's demonstrating -- captured verbatim, in order.
sqlite3 -header -column "$DB_PATH" "PRAGMA foreign_keys = ON;" ".read $SQL_DIR/20_operator_contrasts.sql" \
    > "$RESULTS_DIR/20_operator_contrasts.txt"

echo "Done. Results written to $RESULTS_DIR"
