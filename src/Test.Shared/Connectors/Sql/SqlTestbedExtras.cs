namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Touchstone.Core;

    /// <summary>
    /// Engine-specific testbed cases: defense in depth for statements the classifier cannot see into, and blocking
    /// chains visible through sql_locks.
    /// </summary>
    public static class SqlTestbedExtras
    {
        #region Public-Methods

        /// <summary>
        /// PostgreSQL extras.
        /// </summary>
        /// <param name="suite">Suite identifier.</param>
        /// <param name="target">Target.</param>
        /// <param name="skip">Whether to skip.</param>
        /// <param name="skipReason">Skip reason.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Postgresql(string suite, SqlEngineTarget target, bool skip, string? skipReason)
        {
            SqlConnectorBase connector = target.Factory();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "defense-in-depth-function", "postgresql: a read-classified call to a writing function fails inside the read-only transaction", async ct =>
            {
                ConnectorInvocationContext rw = Context(target, AccessModeEnum.ReadWrite);
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                string table = "hugo_touch_" + suffix;
                string function = "hugo_touch_fn_" + suffix;
                try
                {
                    SqlTestSupport.Ok(await SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":\"CREATE TABLE " + table + " (id int)\"}", rw, ct).ConfigureAwait(false), "create table");
                    string create = "CREATE FUNCTION " + function + "() RETURNS int LANGUAGE sql AS $$ INSERT INTO " + table + " VALUES (1) RETURNING id $$";
                    SqlTestSupport.Ok(await SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":" + SqlTestSupport.Json(create) + "}", rw, ct).ConfigureAwait(false), "create function");

                    string call = "{\"sql\":\"SELECT " + function + "()\"}";
                    EffectClassification c = SqlTestSupport.Tool(connector, "sql_query").Classify(rw, new ToolArguments(call));
                    SqlTestSupport.Check(c.Effect == EffectEnum.Read, "the classifier cannot see inside the function");
                    ToolResult r = await SqlTestSupport.RunAsync(connector, "sql_query", call, rw, ct).ConfigureAwait(false);
                    SqlTestSupport.Error(r, "read_only_violation", "read-classified write under ReadWrite");
                    ToolResult count = await SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":\"SELECT COUNT(*) FROM " + table + "\"}", rw, ct).ConfigureAwait(false);
                    SqlTestSupport.Check(SqlToolSuiteBuilder.Parse(count).Rows[0][0] == "0", "nothing written");
                }
                finally
                {
                    await SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":\"DROP FUNCTION IF EXISTS " + function + "()\"}", rw, CancellationToken.None).ConfigureAwait(false);
                    await SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":\"DROP TABLE IF EXISTS " + table + "\"}", rw, CancellationToken.None).ConfigureAwait(false);
                }
            }, null, skip, skipReason));

            cases.Add(new TestCaseDescriptor(suite, "nextval-read-only", "postgresql: nextval classifies as Write and the read-only transaction refuses it", async ct =>
            {
                ConnectorInvocationContext ro = Context(target, AccessModeEnum.ReadOnly);
                string call = "{\"sql\":\"SELECT nextval('order_number_seq')\"}";
                SqlTestSupport.Check(SqlTestSupport.Tool(connector, "sql_query").Classify(ro, new ToolArguments(call)).Effect == EffectEnum.Write, "classified as Write");
                ToolResult r = await SqlTestSupport.RunAsync(connector, "sql_query", call, ro, ct).ConfigureAwait(false);
                SqlTestSupport.Error(r, "read_only_violation", "nextval under ReadOnly");
            }, null, skip, skipReason));

            cases.Add(Blocking(suite, target, connector, "BEGIN; SELECT order_id FROM orders WHERE order_id = 1 FOR UPDATE; SELECT pg_sleep(5); COMMIT", skip, skipReason));
            return cases;
        }

        /// <summary>
        /// MySQL extras.
        /// </summary>
        /// <param name="suite">Suite identifier.</param>
        /// <param name="target">Target.</param>
        /// <param name="skip">Whether to skip.</param>
        /// <param name="skipReason">Skip reason.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Mysql(string suite, SqlEngineTarget target, bool skip, string? skipReason)
        {
            SqlConnectorBase connector = target.Factory();
            return new List<TestCaseDescriptor>
            {
                Blocking(suite, target, connector, "START TRANSACTION; SELECT order_id FROM orders WHERE order_id = 1 FOR UPDATE; DO SLEEP(5); COMMIT", skip, skipReason)
            };
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Blocking(string suite, SqlEngineTarget target, SqlConnectorBase connector, string holderSql, bool skip, string? skipReason)
        {
            return new TestCaseDescriptor(suite, "blocking-chain", target.Type + ": sql_locks shows a session blocked by another", async ct =>
            {
                ConnectorInvocationContext rw = Context(target, AccessModeEnum.ReadWrite);
                ConnectorInvocationContext batch = Context(target, AccessModeEnum.ReadWrite, true);
                Task<ToolResult> holder = SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":" + SqlTestSupport.Json(holderSql) + "}", batch, ct);
                await Task.Delay(1000, ct).ConfigureAwait(false);
                Task<ToolResult> waiter = SqlTestSupport.RunAsync(connector, "sql_query", "{\"sql\":\"UPDATE orders SET status = status WHERE order_id = 1\"}", rw, ct);
                await Task.Delay(1500, ct).ConfigureAwait(false);
                ToolResult locks = await SqlTestSupport.RunAsync(connector, "sql_locks", "{}", Context(target, AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                ToolResult activity = await SqlTestSupport.RunAsync(connector, "sql_activity", "{}", Context(target, AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(await holder.ConfigureAwait(false), "holder");
                SqlTestSupport.Ok(await waiter.ConfigureAwait(false), "waiter");
                SqlTestSupport.Ok(locks, "locks");
                SqlSectionsResult sections = SqlJson.Deserialize<SqlSectionsResult>(locks.StructuredJson!)!;
                SqlTestSupport.Check(sections.Sections[0].Title == "Blocking" && sections.Sections[0].RowCount >= 1, "blocking pair listed: " + locks.Text);
                SqlTestSupport.Check(sections.Sections[0].Rows[0].Exists(v => v != null && v.Contains("UPDATE orders", StringComparison.OrdinalIgnoreCase)), "blocked statement shown: " + locks.Text);
                SqlTestSupport.Ok(activity, "activity");
                SqlTestSupport.Check(activity.Text.Contains("UPDATE orders", StringComparison.OrdinalIgnoreCase), "waiting statement in activity: " + activity.Text);
            }, null, skip, skipReason);
        }

        private static ConnectorInvocationContext Context(SqlEngineTarget target, AccessModeEnum mode, bool allowBatches = false)
        {
            Dictionary<string, string> settings = target.Settings();
            if (allowBatches) settings["allowMultipleStatements"] = "true";
            ConnectorResourceContext resource = SqlTestSupport.Resource(target.Type, settings, target.Secrets);
            return SqlTestSupport.Context(resource, mode);
        }

        #endregion
    }
}
