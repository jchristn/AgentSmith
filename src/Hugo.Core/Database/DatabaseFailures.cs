namespace Hugo.Core.Database
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using Hugo.Core.Enums;
    using Microsoft.Data.Sqlite;
    using Npgsql;

    /// <summary>
    /// Classifies exceptions from either database provider, so callers can answer a database outage or an exhausted
    /// connection pool with a retryable error instead of an internal error, and can recognize a unique key conflict
    /// from a concurrent insert. Inner and aggregate exceptions are inspected. Thread-safe (stateless).
    /// </summary>
    public static class DatabaseFailures
    {
        #region Public-Methods

        /// <summary>
        /// Classify an exception as a database capacity or availability failure.
        /// </summary>
        /// <param name="exception">Exception, or null.</param>
        /// <returns><see cref="DatabaseFailureEnum.None"/> when the exception is not a capacity or availability failure.</returns>
        public static DatabaseFailureEnum Classify(Exception? exception)
        {
            int depth = 0;
            Exception? current = exception;
            while (current != null && depth++ < 8)
            {
                DatabaseFailureEnum found = ClassifyOne(current);
                if (found != DatabaseFailureEnum.None) return found;
                if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
                {
                    foreach (Exception inner in aggregate.InnerExceptions)
                    {
                        DatabaseFailureEnum nested = Classify(inner);
                        if (nested != DatabaseFailureEnum.None) return nested;
                    }
                    return DatabaseFailureEnum.None;
                }
                current = current.InnerException;
            }
            return DatabaseFailureEnum.None;
        }

        /// <summary>
        /// Whether an exception is a unique key or primary key violation (PostgreSQL 23505, Sqlite constraint codes
        /// 2067 and 1555), for example a concurrent insert of the same name.
        /// </summary>
        /// <param name="exception">Exception, or null.</param>
        /// <returns>True for a unique violation.</returns>
        public static bool IsUniqueViolation(Exception? exception)
        {
            int depth = 0;
            Exception? current = exception;
            while (current != null && depth++ < 8)
            {
                if (current is PostgresException postgres && postgres.SqlState == "23505") return true;
                if (current is SqliteException sqlite && sqlite.SqliteErrorCode == 19 && (sqlite.SqliteExtendedErrorCode == 2067 || sqlite.SqliteExtendedErrorCode == 1555)) return true;
                current = current.InnerException;
            }
            return false;
        }

        #endregion

        #region Private-Methods

        private static DatabaseFailureEnum ClassifyOne(Exception exception)
        {
            if (exception is PostgresException postgres)
            {
                string state = postgres.SqlState ?? String.Empty;
                // Class 53: insufficient resources, including 53300 too_many_connections.
                if (state.StartsWith("53", StringComparison.Ordinal)) return DatabaseFailureEnum.Busy;
                // Class 08: connection exceptions. 57P01 to 57P03: administrator or crash shutdown, cannot connect now.
                if (state.StartsWith("08", StringComparison.Ordinal) || state == "57P01" || state == "57P02" || state == "57P03") return DatabaseFailureEnum.Unavailable;
                return DatabaseFailureEnum.None;
            }

            if (exception is NpgsqlException npgsql)
            {
                // Npgsql reports an exhausted pool as an NpgsqlException wrapping a TimeoutException.
                if (npgsql.InnerException is TimeoutException) return DatabaseFailureEnum.Busy;
                if (npgsql.InnerException is SocketException || npgsql.InnerException is IOException || npgsql.InnerException is EndOfStreamException) return DatabaseFailureEnum.Unavailable;
                if (npgsql.IsTransient) return DatabaseFailureEnum.Unavailable;
                return DatabaseFailureEnum.None;
            }

            if (exception is SqliteException sqlite)
            {
                // 5 SQLITE_BUSY, 6 SQLITE_LOCKED; 14 SQLITE_CANTOPEN, 10 SQLITE_IOERR.
                if (sqlite.SqliteErrorCode == 5 || sqlite.SqliteErrorCode == 6) return DatabaseFailureEnum.Busy;
                if (sqlite.SqliteErrorCode == 14 || sqlite.SqliteErrorCode == 10) return DatabaseFailureEnum.Unavailable;
                return DatabaseFailureEnum.None;
            }

            return DatabaseFailureEnum.None;
        }

        #endregion
    }
}
