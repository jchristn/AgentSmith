namespace Hugo.Connectors.Sql.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>sql_explain</c>: show the execution plan of one statement without running it. <c>analyze</c> (which runs the
    /// statement) is only honored for statements that classify as Read. Thread-safe.
    /// </summary>
    public class SqlExplainTool : SqlToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="cache">Schema cache.</param>
        public SqlExplainTool(SqlDialect dialect, SchemaCache cache) : base(BuildDescriptor(), dialect, cache)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);

            string? sql = arguments.GetString("sql");
            if (String.IsNullOrWhiteSpace(sql)) return ToolResult.Error("invalid_argument", "Argument 'sql' is required.");
            bool analyze = arguments.GetBool("analyze", false);

            StatementClassification c = StatementClassifier.Classify(sql, Dialect.Engine, false);
            if (c.Rejected) return ToolResult.Error("rejected", "sql_explain takes exactly one statement: " + c.Reason + ".");
            if (!c.Parsed) return ToolResult.Error("invalid_argument", "The statement could not be parsed, so it cannot be explained safely: " + c.Reason + ".");
            if (c.PrimaryType.StartsWith("EXPLAIN", StringComparison.Ordinal) || c.PrimaryType == "DESCRIBE")
                return ToolResult.Error("invalid_argument", "Pass the statement itself; sql_explain adds EXPLAIN.");
            if (analyze && c.Effect != EffectEnum.Read)
                return ToolResult.Error("analyze_not_allowed", "analyze executes the statement, so it is only allowed for statements that classify as Read. This statement is " + c.Effect + " (" + c.Reason + "). Run without analyze to see the estimated plan.");

            string? note = null;
            if (analyze && !Dialect.SupportsExplainAnalyze)
            {
                analyze = false;
                note = "analyze is not supported on " + Dialect.DisplayName + "; showing the estimated plan.";
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

            int maxRows = Math.Max(context.Limits.MaxRows, 1);
            bool readOnly = analyze || c.Effect == EffectEnum.Read || !Dialect.ExplainOfWritesNeedsWritableSession;
            return await RunAsync(context, readOnly, async (session, timeoutSeconds, ct) =>
            {
                SqlQueryResult plan = await Dialect.ExplainAsync(session, sql, analyze, parameters, maxRows, timeoutSeconds, ct).ConfigureAwait(false);
                plan.StatementType = c.PrimaryType;
                plan.Note = note;
                return Bounded(context, RenderPlan(plan, analyze), plan, plan.RowCount);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string RenderPlan(SqlQueryResult plan, bool analyze)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(analyze ? "Executed plan (EXPLAIN ANALYZE):\n" : "Estimated plan (statement not executed):\n");
            if (!String.IsNullOrEmpty(plan.Note)) sb.Append(plan.Note).Append('\n');
            if (plan.Columns.Count == 1)
            {
                foreach (List<string?> row in plan.Rows) sb.Append(row.FirstOrDefault() ?? String.Empty).Append('\n');
            }
            else
            {
                sb.Append(plan.RenderTable(2000));
            }
            if (plan.Truncated) sb.Append("[plan truncated at ").Append(plan.RowLimit).Append(" rows]\n");
            return sb.ToString();
        }

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = "sql_explain",
                Title = "Explain SQL",
                Description = "Show the execution plan of one statement without running it (PostgreSQL and MySQL EXPLAIN, SQL Server SHOWPLAN_TEXT, SQLite EXPLAIN QUERY PLAN). "
                    + "Use it before running heavy queries to check for full scans and missing indexes. "
                    + "Set analyze to true to execute and time the statement; that is only allowed for read statements and only on PostgreSQL and MySQL. "
                    + "Pass the statement without the EXPLAIN keyword.",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"sql\":{\"type\":\"string\",\"description\":\"The statement to explain, without EXPLAIN.\"},"
                    + "\"analyze\":{\"type\":\"boolean\",\"description\":\"Execute and measure (read statements only). Default false.\"},"
                    + "\"parameters\":{\"type\":\"object\",\"description\":\"Named parameter values referenced as @name.\",\"additionalProperties\":true}"
                    + "},\"required\":[\"sql\"],\"additionalProperties\":false}",
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Plan text; structured content with the plan rows."
            };
        }

        #endregion
    }
}
