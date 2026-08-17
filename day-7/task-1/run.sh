#!/usr/bin/env bash
# Builds a fresh SQLite database from sql/01_schema.sql + sql/02_seed.sql, then executes
# each query file for real and captures its actual stdout into results/*.txt. Re-run any
# time -- the db file is rebuilt from scratch every time, so output is always current.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_DIR="$SCRIPT_DIR/sql"
RESULTS_DIR="$SCRIPT_DIR/results"
DB_PATH="$SCRIPT_DIR/quotes.db"

rm -f "$DB_PATH"
mkdir -p "$RESULTS_DIR"

sqlite3 "$DB_PATH" ".read $SQL_DIR/01_schema.sql" ".read $SQL_DIR/02_seed.sql"

# $1 sql file, $2 output file, $3 optional cap on data rows (0/omitted = full result)
run_query() {
    local sql_file="$1"
    local out_file="$2"
    local cap="${3:-0}"

    local raw
    raw="$(sqlite3 -header -column "$DB_PATH" "PRAGMA foreign_keys = ON;" ".read $sql_file")"

    if [ "$cap" -gt 0 ]; then
        {
            echo "TOP $cap ROWS"
            echo "$raw" | head -n "$((cap + 2))"
        } > "$out_file"
    else
        echo "$raw" > "$out_file"
    fi
}

run_query "$SQL_DIR/10_inner_join.sql"                  "$RESULTS_DIR/10_inner_join.txt"
run_query "$SQL_DIR/11_left_join.sql"                   "$RESULTS_DIR/11_left_join.txt"
run_query "$SQL_DIR/12_cross_join.sql"                  "$RESULTS_DIR/12_cross_join.txt"
run_query "$SQL_DIR/20_author_quote_summary.sql"        "$RESULTS_DIR/20_author_quote_summary.txt" 10
run_query "$SQL_DIR/21_recursive_cte_influence_chain.sql" "$RESULTS_DIR/21_recursive_cte_influence_chain.txt"

echo "Done. Results written to $RESULTS_DIR"
