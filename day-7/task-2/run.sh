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

run_query() {
    local sql_file="$1"
    local out_file="$2"
    sqlite3 -header -column "$DB_PATH" "PRAGMA foreign_keys = ON;" ".read $sql_file" > "$out_file"
}

run_query "$SQL_DIR/10_row_number_vs_rank.sql" "$RESULTS_DIR/10_row_number_vs_rank.txt"
run_query "$SQL_DIR/11_lead_next_quote.sql"    "$RESULTS_DIR/11_lead_next_quote.txt"
run_query "$SQL_DIR/12_running_total.sql"      "$RESULTS_DIR/12_running_total.txt"
run_query "$SQL_DIR/20_author_quote_windows.sql" "$RESULTS_DIR/20_author_quote_windows.txt"

# Sample rows for the graded query: reuse its exact SQL text (never retyped) as a temp
# view, then filter down to ~12-15 rows chosen to include the single-quote author
# (Callum Reyes), the tied-timestamp pair (Talia Marsh), and the large year-boundary gap
# (Wren Ashby), plus a few ordinary rows (Dorian Fenwick) for contrast.
GRADED_SQL_NO_SEMI="$(sed -e 's/;[[:space:]]*$//' "$SQL_DIR/20_author_quote_windows.sql")"
{
    echo "SAMPLE ROWS (~12-15): Callum Reyes (single-quote author), Talia Marsh (tied timestamps),"
    echo "Wren Ashby (same-day / few-days / year-boundary gaps), and Dorian Fenwick's first 4 (ordinary rows)."
    echo
    sqlite3 -header -column "$DB_PATH" "PRAGMA foreign_keys = ON;" \
        "CREATE TEMP VIEW AuthorQuoteWindows AS
        $GRADED_SQL_NO_SEMI;
        SELECT * FROM AuthorQuoteWindows
        WHERE AuthorName IN ('Callum Reyes','Talia Marsh','Wren Ashby')
           OR (AuthorName = 'Dorian Fenwick' AND CreatedAt <= '2023-03-01 23:59:59')
        ORDER BY AuthorName, RunningQuoteCount;"
} > "$RESULTS_DIR/20_sample_rows.txt"

echo "Done. Results written to $RESULTS_DIR"
