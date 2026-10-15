#!/bin/bash
# Starts SQL Server, waits until it accepts logins, applies sqlserver.sql (idempotent), then keeps the server in the
# foreground. The container healthcheck passes only once the shop tables exist.
set -u

SQLCMD=/opt/mssql-tools18/bin/sqlcmd

/opt/mssql/bin/sqlservr &
server_pid=$!

ready=0
for attempt in $(seq 1 120); do
    if "$SQLCMD" -S 127.0.0.1 -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 2
done

if [ "$ready" = "1" ]; then
    if "$SQLCMD" -S 127.0.0.1 -U sa -P "$MSSQL_SA_PASSWORD" -C -b -i /seed/sqlserver.sql; then
        echo "hugo-testbed: shop seed applied"
    else
        echo "hugo-testbed: shop seed FAILED" >&2
    fi
else
    echo "hugo-testbed: SQL Server did not accept logins in time" >&2
fi

wait "$server_pid"
