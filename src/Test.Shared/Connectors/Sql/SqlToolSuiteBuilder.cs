namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using Touchstone.Core;

    /// <summary>
    /// Builds the tool suite every SQL engine runs against the seeded "shop" schema: connection test, every tool,
    /// row and byte caps, read-only enforcement, batches, timeouts, and cancellation.
    /// </summary>
    public static class SqlToolSuiteBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build the cases for one target.
        /// </summary>
        /// <param name="suite">Suite identifier.</param>
        /// <param name="target">Target.</param>
        /// <param name="skip">Whether to skip every case.</param>
        /// <param name="skipReason">Skip reason.</param>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build(string suite, SqlEngineTarget target, bool skip, string? skipReason)
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            SqlConnectorBase connector = target.Factory();

            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(suite, id, target.Type + ": " + name, body, null, skip, skipReason));
            }

            ConnectorInvocationContext Ctx(AccessModeEnum mode, Dictionary<string, string>? extra = null, int maxRows = 500, int maxBytes = 262144, int timeoutSeconds = 60)
            {
                Dictionary<string, string> settings = target.Settings();
                if (extra != null)
                {
                    foreach (KeyValuePair<string, string> kv in extra) settings[kv.Key] = kv.Value;
                }
                ConnectorResourceContext resource = SqlTestSupport.Resource(target.Type, settings, target.Secrets);
                resource.ConfigurationHash = target.Type + "-" + String.Join(",", settings.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value));
                return SqlTestSupport.Context(resource, mode, timeoutSeconds, maxRows, maxBytes);
            }

            Task<ToolResult> Run(string tool, string json, ConnectorInvocationContext context, CancellationToken token)
            {
                return SqlTestSupport.RunAsync(connector, tool, json, context, token);
            }

            Add("test-connection", "TestConnectionAsync opens a session and reports the server version", async ct =>
            {
                ConnectorInvocationContext c = Ctx(AccessModeEnum.ReadOnly);
                ConnectorValidationResult valid = await connector.ValidateAsync(c.Resource, ct).ConfigureAwait(false);
                SqlTestSupport.Check(valid.IsValid, "settings valid");
                ConnectorHealthResult health = await connector.TestConnectionAsync(c.Resource, ct).ConfigureAwait(false);
                SqlTestSupport.Check(health.Success, "probe succeeded: " + health.ErrorCode + " " + health.Message);
                SqlTestSupport.Check(health.Message.StartsWith("Connected to ", StringComparison.Ordinal) && health.Details["serverVersion"].Length > 0, "version reported: " + health.Message);
                SqlTestSupport.Check(health.Details["readOnlyEnforcement"] == (target.EngineReadOnly ? "session" : "classifier"), "enforcement detail " + health.Details["readOnlyEnforcement"]);
            });

            Add("list-schemas", "sql_list_schemas lists the default schema", async ct =>
            {
                ToolResult r = await Run("sql_list_schemas", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "sql_list_schemas");
                SqlTestSupport.Check(r.Text.Contains("Default schema: " + target.DefaultSchema, StringComparison.Ordinal), "default schema line: " + r.Text);
                SqlQueryResult s = Parse(r);
                SqlTestSupport.Check(s.Rows.Any(row => String.Equals(row[0], target.DefaultSchema, StringComparison.OrdinalIgnoreCase)), "schema row present");
            });

            Add("list-tables", "sql_list_tables lists the shop tables and the view", async ct =>
            {
                ToolResult r = await Run("sql_list_tables", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "sql_list_tables");
                SqlQueryResult s = Parse(r);
                foreach (string t in new[] { "customers", "orders", "order_items", "products" })
                    SqlTestSupport.Check(s.Rows.Any(row => row[0] == t && row[1] == "BASE TABLE"), "table " + t + " in " + r.Text);
                SqlTestSupport.Check(s.Rows.Any(row => row[0] == "customer_order_totals" && row[1] == "VIEW"), "view listed");
                ToolResult filtered = await Run("sql_list_tables", "{\"filter\":\"ORDER\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(filtered).Rows.Count == 3, "filter matches orders, order_items, customer_order_totals: " + filtered.Text);
            });

            Add("describe-table", "sql_describe_table reports columns, keys, indexes, foreign keys, and rows", async ct =>
            {
                ToolResult r = await Run("sql_describe_table", "{\"table\":\"orders\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "sql_describe_table");
                TableDetail t = SqlJson.Deserialize<TableDetail>(r.StructuredJson!)!;
                SqlTestSupport.Check(t.Columns.Select(c => c.ColumnName).SequenceEqual(new[] { "order_id", "customer_id", "status", "ordered_at", "total" }), "columns in order");
                SqlTestSupport.Check(t.Columns[0].IsPrimaryKey && !t.Columns[0].IsNullable && !t.Columns[1].IsPrimaryKey, "primary key and nullability");
                SqlTestSupport.Check(t.ForeignKeys.Any(f => f.ColumnName == "customer_id" && f.ReferencedTable == "customers" && f.ReferencedColumn == "customer_id"), "foreign key");
                SqlTestSupport.Check(t.Indexes.Any(i => i.IndexName == "idx_orders_customer_id" && i.Columns.SequenceEqual(new[] { "customer_id" }) && !i.IsUnique), "secondary index");
                SqlTestSupport.Check(t.RowEstimate.HasValue && t.RowEstimate.Value > 0, "row estimate " + t.RowEstimate);
                SqlTestSupport.Check(r.Text.Contains("Primary key: order_id", StringComparison.Ordinal) && r.Text.Contains("-> ", StringComparison.Ordinal), "text rendering");

                ToolResult qualified = await Run("sql_describe_table", "{\"table\":" + SqlTestSupport.Json(target.DefaultSchema + ".customers") + "}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(qualified, "schema-qualified describe");
                TableDetail customers = SqlJson.Deserialize<TableDetail>(qualified.StructuredJson!)!;
                SqlTestSupport.Check(customers.Indexes.Any(i => i.IsUnique && i.Columns.SequenceEqual(new[] { "email" })), "unique email index");

                ToolResult view = await Run("sql_describe_table", "{\"table\":\"customer_order_totals\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(view, "describe view");
                SqlTestSupport.Check(SqlJson.Deserialize<TableDetail>(view.StructuredJson!)!.TableType == "VIEW", "view type");

                ToolResult missing = await Run("sql_describe_table", "{\"table\":\"no_such_table\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(missing, "not_found", "missing table");
            });

            Add("describe-schema", "sql_describe_schema crawls the schema and caches it", async ct =>
            {
                ConnectorInvocationContext c = Ctx(AccessModeEnum.ReadOnly);
                connector.SchemaCache.Clear();
                ToolResult first = await Run("sql_describe_schema", "{}", c, ct).ConfigureAwait(false);
                SqlTestSupport.Ok(first, "crawl");
                SchemaDetail detail = SqlJson.Deserialize<SchemaDetail>(first.StructuredJson!)!;
                SqlTestSupport.Check(detail.Tables.Count == 5 && detail.Engine == target.Type, "five objects for " + detail.Engine + ": " + detail.Tables.Count);
                SqlTestSupport.Check(detail.Tables.Single(t => t.TableName == "order_items").ForeignKeys.Count == 2, "order_items foreign keys");
                SqlTestSupport.Check(first.Metrics["cached"] == 0, "first crawl not cached");
                ToolResult second = await Run("sql_describe_schema", "{}", c, ct).ConfigureAwait(false);
                SqlTestSupport.Check(second.Metrics["cached"] == 1 && second.Text.Contains("from cache", StringComparison.Ordinal), "second call cached");
                ToolResult named = await Run("sql_describe_schema", "{\"schema\":" + SqlTestSupport.Json(target.DefaultSchema) + "}", c, ct).ConfigureAwait(false);
                SqlTestSupport.Check(named.Metrics["cached"] == 1, "named schema cached");
                ToolResult refreshed = await Run("sql_describe_schema", "{\"refresh\":true}", c, ct).ConfigureAwait(false);
                SqlTestSupport.Check(refreshed.Metrics["cached"] == 0, "refresh recrawls");
                ToolResult disabled = await Run("sql_describe_schema", "{}", Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "schemaCacheMinutes", "0" } }), ct).ConfigureAwait(false);
                ToolResult disabledAgain = await Run("sql_describe_schema", "{}", Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "schemaCacheMinutes", "0" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Check(disabled.Metrics["cached"] == 0 && disabledAgain.Metrics["cached"] == 0, "ttl 0 disables caching");
            });

            Add("query-select", "sql_query returns rows, columns, and structured JSON", async ct =>
            {
                ToolResult r = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) AS n FROM customers\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "count");
                SqlQueryResult s = Parse(r);
                SqlTestSupport.Check(s.Columns.Count == 1 && s.Columns[0].Name == "n" && s.Rows[0][0] == "8", "eight customers: " + r.Text);
                SqlTestSupport.Check(s.StatementType == "SELECT" && !s.Truncated && r.Metrics["rows"] == 1, "metadata");
                SqlTestSupport.Check(r.Text.StartsWith("1 row", StringComparison.Ordinal) && r.Text.Contains("| n |", StringComparison.Ordinal), "text table: " + r.Text);

                ToolResult view = await Run("sql_query", "{\"sql\":\"SELECT full_name, order_count, lifetime_total FROM customer_order_totals WHERE customer_id = 1\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(view, "view query");
                SqlQueryResult v = Parse(view);
                SqlTestSupport.Check(v.Rows[0][0] == "Ada Lovelace" && v.Rows[0][1] == "3" && decimal.Parse(v.Rows[0][2]!, System.Globalization.CultureInfo.InvariantCulture) == 340.25m, "view aggregates: " + view.Text);

                ToolResult nulls = await Run("sql_query", "{\"sql\":\"SELECT NULL AS empty_value, 'x' AS some_value\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(nulls, "nulls");
                SqlTestSupport.Check(Parse(nulls).Rows[0][0] == null && nulls.Text.Contains("NULL", StringComparison.Ordinal), "null rendering");
            });

            Add("query-parameters", "sql_query binds named parameters", async ct =>
            {
                ToolResult r = await Run("sql_query", "{\"sql\":\"SELECT full_name FROM customers WHERE country = @country AND customer_id > @minId ORDER BY customer_id\",\"parameters\":{\"country\":\"US\",\"minId\":2}}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "parameters");
                SqlQueryResult s = Parse(r);
                SqlTestSupport.Check(s.Rows.Select(row => row[0]).SequenceEqual(new[] { "Katherine Johnson", "Margaret Hamilton", "Barbara Liskov" }), "three US customers after id 2: " + r.Text);
                ToolResult injection = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM customers WHERE full_name = @name\",\"parameters\":{\"name\":\"x' OR '1'='1\"}}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(injection).Rows[0][0] == "0", "parameter values are never spliced into SQL");
                ToolResult bad = await Run("sql_query", "{\"sql\":\"SELECT 1\",\"parameters\":{\"bad name\":1}}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(bad, "invalid_argument", "bad parameter name");
            });

            Add("row-caps", "Rows are capped by the setting, the invocation limit, and the maxRows argument", async ct =>
            {
                string json = "{\"sql\":" + SqlTestSupport.Json(target.RowCapSql) + "}";
                ToolResult bySetting = await Run("sql_query", json, Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "maxRows", "100" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(bySetting, "setting cap");
                SqlQueryResult s = Parse(bySetting);
                SqlTestSupport.Check(s.Rows.Count == 100 && s.Truncated && s.RowLimit == 100 && bySetting.Truncated, "100 rows truncated: " + s.Rows.Count);
                SqlTestSupport.Check(bySetting.Text.Contains("truncated at 100", StringComparison.Ordinal), "truncation stated in text");
                ToolResult byLimit = await Run("sql_query", json, Ctx(AccessModeEnum.ReadOnly, null, 20), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(byLimit).Rows.Count == 20, "invocation limit 20");
                ToolResult byArgument = await Run("sql_query", "{\"sql\":" + SqlTestSupport.Json(target.RowCapSql) + ",\"maxRows\":5}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(byArgument).Rows.Count == 5, "argument 5");
                ToolResult all = await Run("sql_query", json, Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "maxRows", "1000" } }, 1000), ct).ConfigureAwait(false);
                SqlQueryResult a = Parse(all);
                SqlTestSupport.Check(a.Rows.Count == 1000 && !a.Truncated && a.Rows[999][0] == "1000", "exactly 1000 rows not truncated");
                ToolResult after = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM orders\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(after).Rows[0][0] == "12", "connection usable after a truncated read");
            });

            Add("byte-limit", "Text and structured output are bounded by the byte limit", async ct =>
            {
                string json = "{\"sql\":" + SqlTestSupport.Json(target.RowCapSql) + "}";
                ToolResult r = await Run("sql_query", json, Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "maxRows", "1000" } }, 1000, 2048), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "byte-limited query");
                SqlTestSupport.Check(r.Truncated && r.OriginalBytes > 2048, "marked truncated with original size");
                SqlTestSupport.Check(Encoding.UTF8.GetByteCount(r.Text) < 2200, "text bounded: " + Encoding.UTF8.GetByteCount(r.Text));
                SqlTestSupport.Check(Encoding.UTF8.GetByteCount(r.StructuredJson!) <= 2048 && Parse(r).Truncated, "structured bounded");
            });

            Add("explain", "sql_explain returns a plan without executing; analyze only for reads", async ct =>
            {
                ToolResult plan = await Run("sql_explain", "{\"sql\":\"SELECT * FROM orders WHERE customer_id = 1\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(plan, "explain select");
                SqlTestSupport.Check(plan.Text.StartsWith("Estimated plan", StringComparison.Ordinal) && plan.Text.Contains(target.PlanMarker, StringComparison.OrdinalIgnoreCase), "plan text: " + plan.Text);
                ToolResult write = await Run("sql_explain", "{\"sql\":\"DELETE FROM order_items WHERE order_id = 1\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(write, "explain of a write without analyze");
                ToolResult count = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM order_items\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(count).Rows[0][0] == "22", "explained DELETE did not run");
                ToolResult analyzeWrite = await Run("sql_explain", "{\"sql\":\"DELETE FROM order_items WHERE order_id = 1\",\"analyze\":true}", Ctx(AccessModeEnum.ReadWrite), ct).ConfigureAwait(false);
                SqlTestSupport.Error(analyzeWrite, "analyze_not_allowed", "analyze of a write");
                ToolResult analyzeRead = await Run("sql_explain", "{\"sql\":\"SELECT COUNT(*) FROM orders\",\"analyze\":true}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(analyzeRead, "analyze of a read");
                SqlTestSupport.Check(target.SupportsAnalyze ? analyzeRead.Text.StartsWith("Executed plan", StringComparison.Ordinal) : analyzeRead.Text.Contains("not supported", StringComparison.Ordinal), "analyze handling: " + analyzeRead.Text);
                ToolResult batch = await Run("sql_explain", "{\"sql\":\"SELECT 1; DROP TABLE orders\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(batch, "rejected", "explain of a batch");
                ToolResult nested = await Run("sql_explain", "{\"sql\":\"EXPLAIN SELECT 1\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(nested, "invalid_argument", "explain of explain");
            });

            Add("activity", "sql_activity shows another session's running statement", async ct =>
            {
                if (!target.HasSessions)
                {
                    ToolResult na = await Run("sql_activity", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                    SqlTestSupport.Ok(na, "activity");
                    SqlTestSupport.Check(na.Text.Contains("not applicable", StringComparison.Ordinal), "not applicable note: " + na.Text);
                    return;
                }
                Task<ToolResult> sleeper = Run("sql_query", "{\"sql\":" + SqlTestSupport.Json(target.ShortSleepSql) + "}", Ctx(AccessModeEnum.ReadOnly), ct);
                await Task.Delay(1500, ct).ConfigureAwait(false);
                ToolResult r = await Run("sql_activity", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                ToolResult idle = await Run("sql_activity", "{\"includeIdle\":true}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                ToolResult slept = await sleeper.ConfigureAwait(false);
                SqlTestSupport.Ok(slept, "sleeper");
                SqlTestSupport.Ok(r, "activity");
                SqlTestSupport.Check(r.Text.Contains(target.SleepMarker, StringComparison.OrdinalIgnoreCase), "running statement visible: " + r.Text);
                SqlTestSupport.Ok(idle, "activity with idle");
                SqlTestSupport.Check(Parse(idle).Rows.Count >= Parse(r).Rows.Count, "includeIdle shows at least as many sessions");
            });

            Add("locks", "sql_locks returns blocking and lock sections", async ct =>
            {
                ToolResult r = await Run("sql_locks", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "locks");
                SqlSectionsResult s = SqlJson.Deserialize<SqlSectionsResult>(r.StructuredJson!)!;
                if (!target.HasSessions)
                {
                    SqlTestSupport.Check(s.Sections.Count == 1 && r.Text.Contains("not applicable", StringComparison.Ordinal), "not applicable: " + r.Text);
                    return;
                }
                SqlTestSupport.Check(s.Sections.Select(x => x.Title).SequenceEqual(new[] { "Blocking", "Locks" }), "sections: " + r.Text);
            });

            Add("table-sizes", "sql_table_sizes lists the shop tables", async ct =>
            {
                ToolResult r = await Run("sql_table_sizes", "{}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "table sizes");
                SqlQueryResult s = Parse(r);
                SqlTestSupport.Check(s.Rows.Any(row => row[0] == "orders") && s.Rows.Any(row => row[0] == "customers"), "tables listed: " + r.Text);
                ToolResult limited = await Run("sql_table_sizes", "{\"limit\":2}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(limited).Rows.Count <= 2, "limit honored");
            });

            Add("read-only-session", "A write in a read-only context is refused (by the engine where supported)", async ct =>
            {
                string insert = "{\"sql\":\"INSERT INTO customers (customer_id, email, full_name, country, created_at) VALUES (99, 'x@example.com', 'X', 'US', '2025-01-01 00:00:00')\"}";
                ToolResult r = await Run("sql_query", insert, Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(r, "read_only_violation", "write under ReadOnly");
                if (target.EngineReadOnly)
                    SqlTestSupport.Check(r.Text.Contains("rejected the statement", StringComparison.Ordinal), "engine rejected it: " + r.Text);
                else
                    SqlTestSupport.Check(r.Text.Contains("was not sent", StringComparison.Ordinal), "classifier refused it: " + r.Text);
                ToolResult classifierOnly = await Run("sql_query", insert, Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "readOnlyEnforcement", "ClassifierOnly" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Error(classifierOnly, "read_only_violation", "classifier-only enforcement");
                SqlTestSupport.Check(classifierOnly.Text.Contains("was not sent", StringComparison.Ordinal), "not sent under ClassifierOnly");
                ToolResult count = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM customers\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(count).Rows[0][0] == "8", "no row was written");
            });

            Add("read-write-round-trip", "Writes succeed under ReadWrite and report affected rows", async ct =>
            {
                string table = "hugo_scratch_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                ConnectorInvocationContext rw = Ctx(AccessModeEnum.ReadWrite);
                try
                {
                    SqlTestSupport.Ok(await Run("sql_query", "{\"sql\":\"CREATE TABLE " + table + " (id INT PRIMARY KEY, label VARCHAR(20))\"}", rw, ct).ConfigureAwait(false), "create");
                    ToolResult insert = await Run("sql_query", "{\"sql\":\"INSERT INTO " + table + " (id, label) VALUES (1, 'a'), (2, 'b')\"}", rw, ct).ConfigureAwait(false);
                    SqlTestSupport.Ok(insert, "insert");
                    SqlTestSupport.Check(Parse(insert).RecordsAffected == 2 && insert.Text.Contains("2 row(s) affected", StringComparison.Ordinal), "affected rows: " + insert.Text);
                    ToolResult select = await Run("sql_query", "{\"sql\":\"SELECT label FROM " + table + " ORDER BY id\"}", rw, ct).ConfigureAwait(false);
                    SqlTestSupport.Check(Parse(select).Rows.Select(row => row[0]).SequenceEqual(new[] { "a", "b" }), "rows read back");
                }
                finally
                {
                    await Run("sql_query", "{\"sql\":\"DROP TABLE " + table + "\"}", rw, CancellationToken.None).ConfigureAwait(false);
                }
            });

            Add("batches", "Multiple statements are rejected unless the resource allows them", async ct =>
            {
                ToolResult rejected = await Run("sql_query", "{\"sql\":\"SELECT 1; SELECT 2\"}", Ctx(AccessModeEnum.ReadWrite), ct).ConfigureAwait(false);
                SqlTestSupport.Error(rejected, "rejected", "batch");
                ToolResult allowed = await Run("sql_query", "{\"sql\":\"SELECT 1 AS a; SELECT 2 AS b\"}", Ctx(AccessModeEnum.ReadWrite, new Dictionary<string, string> { { "allowMultipleStatements", "true" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(allowed, "allowed batch");
                SqlTestSupport.Check(Parse(allowed).Columns[0].Name == "a", "first result set returned");
                ToolResult empty = await Run("sql_query", "{\"sql\":\"-- nothing\"}", Ctx(AccessModeEnum.ReadWrite), ct).ConfigureAwait(false);
                SqlTestSupport.Error(empty, "rejected", "comment only");
            });

            Add("sql-error", "Database errors are tool errors with the engine message", async ct =>
            {
                ToolResult r = await Run("sql_query", "{\"sql\":\"SELECT no_such_column FROM customers\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Error(r, "sql_error", "unknown column");
                SqlTestSupport.Check(r.Text.Contains("no_such_column", StringComparison.OrdinalIgnoreCase), "engine message kept: " + r.Text);
                SqlTestSupport.Check(r.Metrics.ContainsKey("upstreamMs"), "latency recorded on errors");
            });

            Add("timeout", "A statement past the command timeout is cancelled and reported as timeout", async ct =>
            {
                ToolResult? r = null;
                TimeSpan elapsed = await SqlTestSupport.TimeAsync(async () =>
                {
                    r = await Run("sql_query", "{\"sql\":" + SqlTestSupport.Json(target.SlowSql) + "}", Ctx(AccessModeEnum.ReadOnly, new Dictionary<string, string> { { "commandTimeoutSeconds", "1" } }), ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
                SqlTestSupport.Error(r!, "timeout", "slow statement");
                SqlTestSupport.Check(elapsed < TimeSpan.FromSeconds(8), "cancelled promptly: " + elapsed);
                ToolResult invocationTimeout = await Run("sql_query", "{\"sql\":" + SqlTestSupport.Json(target.SlowSql) + "}", Ctx(AccessModeEnum.ReadOnly, null, 500, 262144, 1), ct).ConfigureAwait(false);
                SqlTestSupport.Error(invocationTimeout, "timeout", "invocation timeout smaller than command timeout");
                ToolResult after = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM products\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(after).Rows[0][0] == "10", "next call works after a timeout");
            });

            Add("cancellation", "Cancelling the caller token cancels the statement and throws", async ct =>
            {
                bool cancelled = false;
                TimeSpan elapsed = await SqlTestSupport.TimeAsync(async () =>
                {
                    using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(TimeSpan.FromMilliseconds(700));
                        try
                        {
                            await Run("sql_query", "{\"sql\":" + SqlTestSupport.Json(target.SlowSql) + "}", Ctx(AccessModeEnum.ReadOnly), cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }
                    }
                }).ConfigureAwait(false);
                SqlTestSupport.Check(cancelled, "OperationCanceledException thrown");
                SqlTestSupport.Check(elapsed < TimeSpan.FromSeconds(8), "cancelled promptly: " + elapsed);
                ToolResult after = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM products\"}", Ctx(AccessModeEnum.ReadOnly), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Parse(after).Rows[0][0] == "10", "next call works after cancellation");
            });

            return cases;
        }

        /// <summary>
        /// Deserialize the structured content of a tool result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>Query result.</returns>
        /// <exception cref="InvalidOperationException">Thrown when there is no structured content.</exception>
        public static SqlQueryResult Parse(ToolResult result)
        {
            if (result.IsError || String.IsNullOrEmpty(result.StructuredJson))
                throw new InvalidOperationException("Expected structured content but got " + (result.IsError ? "error " + result.ErrorCode + ": " : String.Empty) + result.Text);
            return SqlJson.Deserialize<SqlQueryResult>(result.StructuredJson) ?? throw new InvalidOperationException("Null structured content.");
        }

        #endregion
    }
}
