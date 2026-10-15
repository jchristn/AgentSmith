#!/bin/bash
# Creates a seeded SQLite "shop" database for manual testing. Tests build their own temporary copy from sqlite.sql,
# so no .db file is committed.
#
#   docker/testbed/sql/seed/create-sqlite.sh /path/to/shop.db
set -euo pipefail

if [ "$#" -ne 1 ]; then
    echo "usage: $0 <database-file>" >&2
    exit 2
fi

target="$1"
seed="$(cd "$(dirname "$0")" && pwd)/sqlite.sql"

if [ -e "$target" ]; then
    echo "refusing to overwrite existing file: $target" >&2
    exit 1
fi

if ! command -v sqlite3 > /dev/null 2>&1; then
    echo "sqlite3 is required (brew install sqlite, or apt-get install sqlite3)" >&2
    exit 1
fi

sqlite3 "$target" < "$seed"
echo "created $target"
