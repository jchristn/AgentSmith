namespace Hugo.Core.Database.Telemetry
{
    using System;
    using System.Collections.Concurrent;
    using System.Data;
    using System.Data.Common;
    using System.Diagnostics;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Database instrumentation used by the ADO.NET driver's central execution methods (P13.24, P13.25): a duration
    /// histogram and error counter per provider, operation, and table, connections in use, Sqlite write-lock waits,
    /// transaction retries, and a client span per statement named by <see cref="DatabaseStatement"/> (never the SQL
    /// text or parameter values). Statement spans are only started inside an existing trace, so background polling
    /// does not create root spans for every query. Thread-safe.
    /// </summary>
    public static class DatabaseTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of distinct SQL texts whose parsed names are cached. Minimum 16, maximum 100000, default 4096.
        /// Beyond it statements are parsed on every call.
        /// </summary>
        public static int MaxCachedStatements
        {
            get
            {
                return _MaxCachedStatements;
            }
            set
            {
                _MaxCachedStatements = Math.Clamp(value, 16, 100000);
            }
        }

        /// <summary>
        /// Statement name for an explicit transaction.
        /// </summary>
        public static readonly DatabaseStatement Transaction = new DatabaseStatement("transaction", "none");

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, DatabaseStatement> _Statements = new ConcurrentDictionary<string, DatabaseStatement>(StringComparer.Ordinal);
        private static int _MaxCachedStatements = 4096;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The provider label for a connection: postgresql, sqlite, or the connection type name.
        /// </summary>
        /// <param name="connection">Connection.</param>
        /// <returns>Provider label.</returns>
        public static string Provider(DbConnection? connection)
        {
            if (connection == null) return "unknown";
            string name = connection.GetType().Name;
            if (name.StartsWith("Npgsql", StringComparison.Ordinal)) return "postgresql";
            if (name.StartsWith("Sqlite", StringComparison.Ordinal)) return "sqlite";
            return name.ToLowerInvariant();
        }

        /// <summary>
        /// Name a SQL statement.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <returns>Statement name.</returns>
        public static DatabaseStatement Describe(string? sql)
        {
            if (String.IsNullOrEmpty(sql)) return new DatabaseStatement("other", "none");
            if (_Statements.TryGetValue(sql, out DatabaseStatement? cached)) return cached;
            DatabaseStatement parsed = Parse(sql);
            if (_Statements.Count < _MaxCachedStatements) _Statements.TryAdd(sql, parsed);
            return parsed;
        }

        /// <summary>
        /// Start a statement span when there is a current trace to nest it under.
        /// </summary>
        /// <param name="statement">Statement name.</param>
        /// <param name="provider">Provider label.</param>
        /// <returns>Span, or null.</returns>
        public static Activity? Start(DatabaseStatement statement, string provider)
        {
            if (statement == null) return null;
            Activity? activity = HugoTracing.Start("db " + statement.Name, ActivityKind.Client, true);
            if (activity != null)
            {
                activity.SetTag("db.system", provider);
                activity.SetTag("db.operation.name", statement.Operation);
                activity.SetTag("db.collection.name", statement.Table);
                activity.SetTag("db.query.summary", statement.Name);
            }
            return activity;
        }

        /// <summary>
        /// End a statement: record its duration, count an error, and close the span.
        /// </summary>
        /// <param name="activity">Span from <see cref="Start"/>, or null.</param>
        /// <param name="statement">Statement name.</param>
        /// <param name="provider">Provider label.</param>
        /// <param name="startTimestamp">Stopwatch timestamp taken before the statement.</param>
        /// <param name="exception">Exception the statement threw, or null.</param>
        public static void End(Activity? activity, DatabaseStatement statement, string provider, long startTimestamp, Exception? exception)
        {
            if (statement == null) return;
            try
            {
                string result = exception == null ? "ok" : exception is OperationCanceledException ? "cancelled" : "error";
                if (exception != null && result == "error")
                {
                    HugoTracing.Fail(activity, exception);
                    HugoMetrics.Count(HugoMetricCatalog.DbErrors, 1, ErrorTags(provider, statement, exception));
                }
                activity?.Dispose();
                if (HugoMetrics.Enabled(HugoMetricCatalog.DbDuration))
                {
                    System.Diagnostics.TagList tags = HugoMetrics.Tags("provider", provider, "operation", statement.Operation, "table", statement.Table, "result", result);
                    HugoMetrics.Observe(HugoMetricCatalog.DbDuration, HugoMetrics.SecondsSince(startTimestamp), tags);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Count an opened connection and arrange for its close to be counted.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        public static void TrackConnection(DbConnection connection)
        {
            if (connection == null || !HugoMetrics.Enabled(HugoMetricCatalog.DbConnections)) return;
            string provider = Provider(connection);
            System.Diagnostics.TagList tags = HugoMetrics.Tags("provider", provider);
            HugoMetrics.Adjust(HugoMetricCatalog.DbConnections, 1, tags);
            StateChangeEventHandler? handler = null;
            handler = (sender, e) =>
            {
                if (e.CurrentState != ConnectionState.Closed && e.CurrentState != ConnectionState.Broken) return;
                connection.StateChange -= handler;
                HugoMetrics.Adjust(HugoMetricCatalog.DbConnections, -1, HugoMetrics.Tags("provider", provider));
            };
            connection.StateChange += handler;
        }

        /// <summary>
        /// Record a wait for the provider's write lock.
        /// </summary>
        /// <param name="provider">Provider label.</param>
        /// <param name="startTimestamp">Stopwatch timestamp taken before waiting.</param>
        public static void WriteLockWaited(string provider, long startTimestamp)
        {
            HugoMetrics.Observe(HugoMetricCatalog.DbWriteLockWait, HugoMetrics.SecondsSince(startTimestamp), "provider", provider);
        }

        /// <summary>
        /// Count a transaction retried after a transient conflict.
        /// </summary>
        /// <param name="provider">Provider label.</param>
        public static void Retried(string provider)
        {
            HugoMetrics.Count(HugoMetricCatalog.DbRetries, "provider", provider);
        }

        #endregion

        #region Private-Methods

        private static System.Diagnostics.TagList ErrorTags(string provider, DatabaseStatement statement, Exception exception)
        {
            System.Diagnostics.TagList tags = HugoMetrics.Tags("provider", provider, "operation", statement.Operation, "table", statement.Table, "error", exception.GetType().Name);
            return tags;
        }

        private static DatabaseStatement Parse(string sql)
        {
            string text = sql.TrimStart();
            string verb = FirstWord(text, 0, out int afterVerb).ToLowerInvariant();
            switch (verb)
            {
                case "select":
                    return new DatabaseStatement("select", TableAfter(text, " from ", afterVerb));
                case "with":
                    return new DatabaseStatement(MainVerb(text), TableAfter(text, " from ", afterVerb));
                case "insert":
                    string inserted = TableAfter(text, " into ", 0);
                    return new DatabaseStatement(text.IndexOf(" on conflict", StringComparison.OrdinalIgnoreCase) >= 0 ? "upsert" : "insert", inserted);
                case "update":
                    return new DatabaseStatement("update", Clean(FirstWord(text, afterVerb, out int _)));
                case "delete":
                    return new DatabaseStatement("delete", TableAfter(text, " from ", 0));
                case "create":
                case "alter":
                case "drop":
                    return new DatabaseStatement("ddl", "schema");
                case "pragma":
                case "set":
                    return new DatabaseStatement("config", "none");
                case "begin":
                case "commit":
                case "rollback":
                    return new DatabaseStatement("transaction", "none");
                default:
                    if (verb.StartsWith("select", StringComparison.Ordinal)) return new DatabaseStatement("select", TableAfter(text, " from ", 0));
                    return new DatabaseStatement("other", "none");
            }
        }

        private static string MainVerb(string text)
        {
            int close = text.LastIndexOf(')');
            string tail = close >= 0 && close + 1 < text.Length ? text.Substring(close + 1).TrimStart() : String.Empty;
            string verb = FirstWord(tail, 0, out int _).ToLowerInvariant();
            if (verb == "update" || verb == "delete" || verb == "insert") return verb;
            return "select";
        }

        private static string TableAfter(string text, string keyword, int from)
        {
            int index = text.IndexOf(keyword, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                // A keyword at a line start ("\nFROM runs") has no leading space.
                index = text.IndexOf(keyword.TrimStart(), from, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return "none";
                index -= 1;
            }
            int start = index + keyword.Length;
            if (start >= text.Length) return "none";
            return Clean(FirstWord(text, start, out int _));
        }

        private static string FirstWord(string text, int start, out int end)
        {
            int i = start;
            while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
            int begin = i;
            while (i < text.Length && !Char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ',' && text[i] != ';') i++;
            end = i;
            return begin < text.Length ? text.Substring(begin, i - begin) : String.Empty;
        }

        private static string Clean(string identifier)
        {
            if (String.IsNullOrEmpty(identifier)) return "none";
            int dot = identifier.LastIndexOf('.');
            string name = dot >= 0 ? identifier.Substring(dot + 1) : identifier;
            name = name.Trim('"', '`', '[', ']').ToLowerInvariant();
            if (name.Length == 0 || name.StartsWith("(", StringComparison.Ordinal)) return "none";
            return name;
        }

        #endregion
    }
}
