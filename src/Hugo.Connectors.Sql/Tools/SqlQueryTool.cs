namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_query</c>: run one SQL statement, classified per call, with a row cap and named parameters.
    /// Thread-safe.
    /// </summary>
    public class SqlQueryTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlQueryTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string? sql = arguments?.GetString("sql");
            bool allowMultiple = context != null && context.Resource.Settings.GetBool(SqlSettingKeys.AllowMultipleStatements, false);
            StatementClassification c = StatementClassifier.Classify(sql, Dialect.Engine, allowMultiple);
            string summary = Summarize(context!, arguments!);
            if (c.Rejected)
            {
                EffectClassification rejected = EffectClassification.Reject(c.Reason);
                rejected.ApprovalSummary = summary;
                return rejected;
            }
            return new EffectClassification(c.Effect, c.Reason, summary);
        }

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);

            string? sql = arguments.GetString("sql");
            if (String.IsNullOrWhiteSpace(sql)) return ToolResult.Error("invalid_argument", "Argument 'sql' is required.");

            SqlResourceOptions options = SqlResourceOptions.FromResource(context.Resource);
            StatementClassification c = StatementClassifier.Classify(sql, Dialect.Engine, options.AllowMultipleStatements);
            if (c.Rejected) return ToolResult.Error("rejected", "Statement rejected: " + c.Reason + ".");

            bool readOnly = context.AccessMode == AccessModeEnum.ReadOnly || c.Effect == EffectEnum.Read;
            if (context.AccessMode == AccessModeEnum.ReadOnly && c.Effect != EffectEnum.Read)
            {
                bool engineEnforces = options.ReadOnlyEnforcement == ReadOnlyEnforcementEnum.Session && Dialect.SupportsSessionReadOnly;
                if (!engineEnforces)
                {
                    return ToolResult.Error(
                        "read_only_violation",
                        "This binding is read-only and the statement classifies as " + c.Effect + " (" + c.Reason + "). "
                        + Dialect.DisplayName + " read-only access is enforced by the classifier on this resource, so the statement was not sent.");
                }
            }

            List<SqlParameterValue> parameters;
            try
            {
                parameters = SqlParameterParser.Parse(arguments.GetRawJson("parameters"));
            }
            catch (ConnectorException e)
            {
                return ToolResult.Error(e.Code, e.Message);
            }

            int maxRows = EffectiveMaxRows(context, arguments);
            context.Resource.Logger.Debug("sql_query on " + context.Resource.Name + ": " + c.PrimaryType + " (" + c.Effect + "), read-only session " + readOnly);

            return await RunAsync(context, readOnly, async (session, timeoutSeconds, ct) =>
            {
                SqlQueryResult result = await SqlExecutor.ExecuteAsync(session, sql, parameters, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                result.StatementType = c.PrimaryType;
                return Bounded(context, RenderQuery(result), result, result.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string Summarize(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string target = context == null ? String.Empty : (String.IsNullOrEmpty(context.BindingAlias) ? context.Resource.Name : context.BindingAlias);
            string sql = (arguments?.GetString("sql") ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (sql.Length > 300) sql = sql.Substring(0, 297) + "...";
            return "Run SQL on " + (String.IsNullOrEmpty(target) ? Dialect.DisplayName : target) + ": " + sql;
        }

        #endregion

        #region Private-Methods

        private static string RenderQuery(SqlQueryResult result)
        {
            StringBuilder sb = new StringBuilder();
            if (result.Columns.Count == 0)
            {
                sb.Append("Statement executed");
                if (result.RecordsAffected.HasValue) sb.Append("; ").Append(result.RecordsAffected.Value).Append(" row(s) affected");
                sb.Append(" in ").Append(result.ElapsedMs.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" ms.\n");
                return sb.ToString();
            }
            sb.Append(result.RowCount).Append(result.RowCount == 1 ? " row" : " rows");
            if (result.Truncated) sb.Append(" (truncated at ").Append(result.RowLimit).Append(" rows; more exist, so aggregate or filter in SQL)");
            sb.Append(" in ").Append(result.ElapsedMs.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" ms.\n");
            sb.Append(result.RenderTable());
            return sb.ToString();
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_query",
                Title = "Run SQL query",
                Description = "Run one SQL statement and return rows as a table plus structured columns and rows. Every call is classified: "
                    + "SELECT, SHOW, EXPLAIN, and WITH ... SELECT are reads; INSERT, UPDATE, MERGE, CREATE, and similar are writes; "
                    + "DELETE, DROP, TRUNCATE, and ALTER are destructive and may require approval or be denied by the binding's access mode. "
                    + "Results are capped (default 500 rows), so select explicit columns, filter, aggregate, and add LIMIT/TOP. "
                    + "Bind values with the parameters object and refer to them as @name in the SQL instead of inlining literals. "
                    + "Send one statement per call.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"sql\":{\"type\":\"string\",\"description\":\"One SQL statement.\"},"
                    + "\"parameters\":{\"type\":\"object\",\"description\":\"Named parameter values referenced as @name in the SQL. Values may be strings, numbers, booleans, or null.\",\"additionalProperties\":true},"
                    + "\"maxRows\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100000,\"description\":\"Optional lower row cap for this call.\"}"
                    + "},\"required\":[\"sql\"],\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                ClassifiedPerCall = true,
                OutputDescription = "Text table with a row count and truncation note; structured content { columns: [{name, type}], rows: [[...]], rowCount, rowLimit, truncated, recordsAffected, elapsedMs, statementType }."
            };
        }

        #endregion
    }
}
