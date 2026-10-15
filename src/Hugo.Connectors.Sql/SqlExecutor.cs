namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Executes SQL on a <see cref="SqlSession"/> and reads a row-capped result. Stops reading at the cap and cancels
    /// the rest of the result on the server instead of draining it. Thread-safe (sessions are not).
    /// </summary>
    public static class SqlExecutor
    {
        #region Public-Methods

        /// <summary>
        /// Execute SQL and read the first result set that has columns, up to <paramref name="maxRows"/> rows.
        /// </summary>
        /// <param name="session">Open session.</param>
        /// <param name="sql">SQL text.</param>
        /// <param name="parameters">Named parameters, or null.</param>
        /// <param name="maxRows">Row cap. Minimum 1.</param>
        /// <param name="commandTimeoutSeconds">Driver command timeout in seconds; 0 keeps the connection default.</param>
        /// <param name="token">Cancellation token; cancelling it cancels the command on the server.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when session or sql is null.</exception>
        /// <exception cref="DbException">Thrown for database errors.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task<SqlQueryResult> ExecuteAsync(SqlSession session, string sql, IList<SqlParameterValue>? parameters, int maxRows, int commandTimeoutSeconds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (String.IsNullOrWhiteSpace(sql)) throw new ArgumentNullException(nameof(sql));
            if (maxRows < 1) maxRows = 1;
            token.ThrowIfCancellationRequested();

            SqlQueryResult result = new SqlQueryResult { RowLimit = maxRows };
            Stopwatch stopwatch = Stopwatch.StartNew();

            using (DbCommand command = session.CreateCommand(sql, commandTimeoutSeconds))
            {
                if (parameters != null)
                {
                    foreach (SqlParameterValue p in parameters) command.Parameters.Add(session.Dialect.CreateParameter(command, p));
                }

                using (CancellationTokenRegistration registration = session.Dialect.RegisterInterrupt(session.Connection, command, token))
                {
                    DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                    bool truncated = false;
                    try
                    {
                        bool captured = false;
                        do
                        {
                            if (captured || reader.FieldCount == 0) continue;
                            captured = true;
                            ReadColumns(reader, result);
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                            {
                                if (result.Rows.Count >= maxRows)
                                {
                                    truncated = true;
                                    break;
                                }
                                result.Rows.Add(ReadRow(reader));
                            }
                            if (truncated) break;
                        }
                        while (await reader.NextResultAsync(token).ConfigureAwait(false));

                        if (!truncated)
                        {
                            await reader.CloseAsync().ConfigureAwait(false);
                            int affected = reader.RecordsAffected;
                            if (affected >= 0) result.RecordsAffected = affected;
                        }
                    }
                    finally
                    {
                        if (truncated) session.Dialect.CancelRemainder(command);
                        try
                        {
                            await reader.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (DbException) when (truncated)
                        {
                        }
                    }
                    result.Truncated = truncated;
                }
            }

            stopwatch.Stop();
            result.ElapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        #endregion

        #region Private-Methods

        private static void ReadColumns(DbDataReader reader, SqlQueryResult result)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                if (String.IsNullOrWhiteSpace(name)) name = "column" + (i + 1);
                string unique = name;
                int suffix = 1;
                while (!seen.Add(unique))
                {
                    suffix++;
                    unique = name + "_" + suffix;
                }

                string type;
                try
                {
                    type = reader.GetDataTypeName(i);
                }
                catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException || e is IndexOutOfRangeException)
                {
                    type = String.Empty;
                }
                result.Columns.Add(new SqlColumnInfo(unique, type));
            }
        }

        private static List<string?> ReadRow(DbDataReader reader)
        {
            List<string?> row = new List<string?>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                try
                {
                    row.Add(reader.IsDBNull(i) ? null : SqlValueFormatter.Format(reader.GetValue(i)));
                }
                catch (Exception e) when (e is InvalidCastException || e is OverflowException || e is FormatException || e is NotSupportedException)
                {
                    row.Add("(unreadable " + SafeTypeName(reader, i) + " value)");
                }
            }
            return row;
        }

        private static string SafeTypeName(DbDataReader reader, int ordinal)
        {
            try
            {
                return reader.GetDataTypeName(ordinal);
            }
            catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException)
            {
                return "unknown";
            }
        }

        #endregion
    }
}
