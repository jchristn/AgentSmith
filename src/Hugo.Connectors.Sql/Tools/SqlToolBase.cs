namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Shared plumbing for SQL tools: options, timeouts, cancellation, sessions, error mapping, and output limits.
    /// Thread-safe: no per-call state is kept in fields.
    /// </summary>
    public abstract class SqlToolBase : ConnectorToolBase
    {
        #region Public-Members

        /// <summary>
        /// Dialect.
        /// </summary>
        public SqlDialect Dialect { get; }

        /// <summary>
        /// Schema cache shared by the connector's tools.
        /// </summary>
        public SchemaCache Cache { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Descriptor.</param>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        protected SqlToolBase(ToolDescriptor descriptor, SqlDialect dialect, SchemaCache cache) : base(descriptor)
        {
            Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Open a session and run a body with the effective timeout. Timeouts become a <c>timeout</c> tool error,
        /// caller cancellation propagates as <see cref="OperationCanceledException"/>, connector errors keep their code,
        /// and database errors become <c>sql_error</c> (or <c>read_only_violation</c> when the engine refused a write).
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="readOnly">Whether the call must run read-only.</param>
        /// <param name="body">Body receiving the session, the driver timeout in seconds, and the effective token.</param>
        /// <param name="token">Caller token.</param>
        /// <returns>Result with <c>upstreamMs</c> recorded.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
        protected async Task<ToolResult> RunAsync(ConnectorInvocationContext context, bool readOnly, Func<SqlSession, int, CancellationToken, Task<ToolResult>> body, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(body);
            token.ThrowIfCancellationRequested();

            SqlResourceOptions options = SqlResourceOptions.FromResource(context.Resource);
            int timeoutSeconds = EffectiveTimeoutSeconds(context, options);
            Stopwatch stopwatch = Stopwatch.StartNew();

            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    ToolResult result;
                    try
                    {
                        await using (SqlSession session = await SqlSession.OpenAsync(Dialect, options, readOnly, linked.Token).ConfigureAwait(false))
                        {
                            result = await body(session, timeoutSeconds + 2, linked.Token).ConfigureAwait(false);
                        }

                        // Some engines end an interrupted statement normally (MySQL SLEEP returns 1 when killed), so a
                        // fired timeout or cancellation always wins over whatever partial result came back.
                        token.ThrowIfCancellationRequested();
                        if (timeout.IsCancellationRequested) throw new OperationCanceledException("The SQL call timed out.", timeout.Token);
                    }
                    catch (Exception e) when (token.IsCancellationRequested && !(e is OperationCanceledException))
                    {
                        throw new OperationCanceledException("The SQL call was cancelled.", e, token);
                    }
                    catch (Exception e) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        context.Resource.Logger.Warn(Descriptor.Name + " timed out after " + timeoutSeconds + "s on " + context.Resource.Name + ": " + e.GetType().Name);
                        result = ToolResult.Error("timeout", "The statement exceeded the " + timeoutSeconds + " second timeout and was cancelled on the server. Narrow the query (filters, LIMIT/TOP, aggregates) or check sql_activity and sql_locks for contention.");
                    }
                    catch (ConnectorException e)
                    {
                        result = ToolResult.Error(e.Code, e.Message);
                    }
                    catch (DbException e)
                    {
                        if (Dialect.IsReadOnlyViolation(e))
                            result = ToolResult.Error("read_only_violation", Dialect.DisplayName + " rejected the statement because this call runs in a read-only session: " + e.Message);
                        else
                            result = ToolResult.Error("sql_error", Dialect.DisplayName + " error: " + e.Message);
                    }
                    stopwatch.Stop();
                    result.Metrics["upstreamMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
                    return result;
                }
            }
        }

        /// <summary>
        /// Effective timeout: the smaller of the resource command timeout and the invocation timeout.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="options">Options.</param>
        /// <returns>Seconds, at least 1.</returns>
        protected static int EffectiveTimeoutSeconds(ConnectorInvocationContext context, SqlResourceOptions options)
        {
            int invocation = (int)Math.Ceiling(context.Timeout.TotalSeconds);
            return Math.Max(1, Math.Min(options.CommandTimeoutSeconds, invocation));
        }

        /// <summary>
        /// Effective row cap: the smaller of the resource setting, the invocation limit, and an optional
        /// <c>maxRows</c> argument.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Rows, at least 1.</returns>
        protected static int EffectiveMaxRows(ConnectorInvocationContext context, ToolArguments arguments)
        {
            SqlResourceOptions options = SqlResourceOptions.FromResource(context.Resource);
            int cap = Math.Min(options.MaxRows, context.Limits.MaxRows);
            if (arguments != null && arguments.Has("maxRows")) cap = Math.Min(cap, arguments.GetInt("maxRows", cap, 1, 100000));
            return Math.Max(1, cap);
        }

        /// <summary>
        /// Resolve the schema argument, falling back to the resource or engine default.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Schema.</returns>
        protected string ResolveSchema(SqlSession session, ToolArguments arguments)
        {
            string? schema = arguments?.GetString("schema");
            if (!String.IsNullOrWhiteSpace(schema)) return schema.Trim();
            return Dialect.DefaultSchema(session.Options, session.Connection);
        }

        /// <summary>
        /// Build a bounded result from text and a structured payload. The text is cut to the byte limit; the
        /// structured rows are trimmed until the JSON fits the same limit.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="text">Text for the LLM.</param>
        /// <param name="structured">Structured payload, or null.</param>
        /// <param name="rows">Row count for metrics.</param>
        /// <returns>Result.</returns>
        protected static ToolResult Bounded(ConnectorInvocationContext context, string text, SqlQueryResult? structured, int rows)
        {
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(text, context.Limits.MaxBytes, 0, result);
            if (structured != null)
            {
                string json = SqlJson.Serialize(structured);
                int limit = context.Limits.MaxBytes;
                while (Encoding.UTF8.GetByteCount(json) > limit && structured.Rows.Count > 0)
                {
                    int keep = structured.Rows.Count / 2;
                    structured.Rows.RemoveRange(keep, structured.Rows.Count - keep);
                    structured.Truncated = true;
                    result.Truncated = true;
                    json = SqlJson.Serialize(structured);
                }
                result.StructuredJson = json;
                if (structured.Truncated) result.Truncated = true;
            }
            result.Metrics["rows"] = rows;
            return result;
        }

        /// <summary>
        /// Build a bounded result from text and an arbitrary structured payload.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="context">Invocation context.</param>
        /// <param name="text">Text for the LLM.</param>
        /// <param name="payload">Payload.</param>
        /// <param name="rows">Row count for metrics.</param>
        /// <returns>Result; the payload is dropped when its JSON exceeds the byte limit.</returns>
        protected static ToolResult BoundedPayload<T>(ConnectorInvocationContext context, string text, T payload, int rows)
        {
            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(text, context.Limits.MaxBytes, 0, result);
            string json = SqlJson.Serialize(payload);
            if (Encoding.UTF8.GetByteCount(json) <= context.Limits.MaxBytes) result.StructuredJson = json;
            else result.Truncated = true;
            result.Metrics["rows"] = rows;
            return result;
        }

        /// <summary>
        /// Render a result as text: a summary line, an optional note, and the table.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="noun">Noun for the summary (for example <c>row</c>).</param>
        /// <returns>Text.</returns>
        protected static string Render(SqlQueryResult result, string noun)
        {
            StringBuilder sb = new StringBuilder();
            if (!String.IsNullOrEmpty(result.Title)) sb.Append("## ").Append(result.Title).Append('\n');
            if (!String.IsNullOrEmpty(result.Note)) sb.Append(result.Note).Append('\n');
            if (result.Columns.Count > 0)
            {
                sb.Append(result.RowCount).Append(' ').Append(noun).Append(result.RowCount == 1 ? String.Empty : "s");
                if (result.Truncated) sb.Append(" (truncated at ").Append(result.RowLimit).Append("; more exist)");
                sb.Append('\n');
                if (result.RowCount > 0) sb.Append(result.RenderTable());
            }
            else if (String.IsNullOrEmpty(result.Note))
            {
                sb.Append("No rows.\n");
            }
            return sb.ToString();
        }

        #endregion
    }
}
