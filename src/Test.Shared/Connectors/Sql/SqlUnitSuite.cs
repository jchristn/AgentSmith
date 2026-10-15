namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using Touchstone.Core;

    /// <summary>
    /// Unit suite for the SQL connectors: descriptors, settings, validation, connection strings, parameters, value
    /// formatting, classification through the tool, and the schema cache. Needs no database.
    /// </summary>
    public static class SqlUnitSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            const string suite = "connectors.sql.unit";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "descriptors", "Four connectors share family sql, category Database, icon database, and nine tools", ct =>
            {
                List<SqlConnectorBase> connectors = AllConnectors();
                string[] types = { "sqlite", "mysql", "sqlserver", "postgresql" };
                string[] tools = { "sql_list_schemas", "sql_list_tables", "sql_describe_table", "sql_describe_schema", "sql_query", "sql_explain", "sql_activity", "sql_table_sizes", "sql_locks" };
                SqlTestSupport.Check(connectors.Select(c => c.Descriptor.Type).SequenceEqual(types), "types");
                foreach (SqlConnectorBase c in connectors)
                {
                    ConnectorDescriptor d = c.Descriptor;
                    SqlTestSupport.Check(d.Family == "sql", d.Type + " family");
                    SqlTestSupport.Check(d.Category == ConnectorCategoryEnum.Database, d.Type + " category");
                    SqlTestSupport.Check(d.IconKey == "database", d.Type + " icon");
                    SqlTestSupport.Check(d.DefaultInstructionsKey == "connector." + d.Type + ".instructions", d.Type + " instructions key");
                    SqlTestSupport.Check(d.DefaultInstructions.Length > 500 && d.DefaultInstructions.Contains("sql_query", StringComparison.Ordinal), d.Type + " instructions text");
                    SqlTestSupport.Check(!d.DefaultInstructions.Contains((char)0x2014), d.Type + " instructions contain no em-dash");
                    SqlTestSupport.Check(c.Tools.Select(t => t.Descriptor.Name).SequenceEqual(tools), d.Type + " tools");
                    SqlTestSupport.Check(c.Tools.Single(t => t.Descriptor.Name == "sql_query").Descriptor.ClassifiedPerCall, d.Type + " sql_query classified per call");
                    SqlTestSupport.Check(c.Tools.Where(t => t.Descriptor.Name != "sql_query").All(t => t.Descriptor.BaseEffect == EffectEnum.Read), d.Type + " other tools are Read");
                    foreach (IConnectorTool tool in c.Tools)
                    {
                        using (System.Text.Json.JsonDocument schema = System.Text.Json.JsonDocument.Parse(tool.Descriptor.InputSchemaJson))
                        {
                            SqlTestSupport.Check(schema.RootElement.GetProperty("type").GetString() == "object", tool.Descriptor.Name + " schema is an object");
                        }
                    }
                }
                for (int i = 0; i < tools.Length; i++)
                {
                    HashSet<string> schemas = new HashSet<string>(connectors.Select(c => c.Tools[i].Descriptor.InputSchemaJson));
                    SqlTestSupport.Check(schemas.Count == 1, tools[i] + " has one input schema across the family");
                }
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "settings-and-slots", "Settings schemas and secret slots follow plan section 9.1", ct =>
            {
                ConnectorDescriptor pg = new PostgresqlConnector().Descriptor;
                foreach (string key in new[] { "host", "port", "database", "username", "schema", "sslMode", "applicationName", "commandTimeoutSeconds", "maxRows", "readOnlyEnforcement", "allowMultipleStatements", "schemaCacheMinutes" })
                    SqlTestSupport.Check(pg.SettingsSchema.Find(key) != null, "postgresql setting " + key);
                SqlTestSupport.Check(pg.SettingsSchema.Find("port")!.DefaultValue == "5432", "pg port default");
                SqlTestSupport.Check(pg.SettingsSchema.Find("commandTimeoutSeconds")!.DefaultValue == "30", "timeout default");
                SqlTestSupport.Check(pg.SettingsSchema.Find("maxRows")!.DefaultValue == "500", "maxRows default");
                SqlTestSupport.Check(pg.SettingsSchema.Find("applicationName")!.DefaultValue == "hugo", "application name default");
                SqlTestSupport.Check(pg.SettingsSchema.Find("readOnlyEnforcement")!.AllowedValues.SequenceEqual(new[] { "Session", "ClassifierOnly" }), "enforcement values");
                SqlTestSupport.Check(pg.SettingsSchema.Find("schemaCacheMinutes")!.DefaultValue == "10", "cache default");
                SqlTestSupport.Check(pg.SecretSlots.Select(s => s.Name).SequenceEqual(new[] { "password", "connectionString" }), "pg slots");
                SqlTestSupport.Check(pg.SecretSlots[0].AcceptedKinds.Single() == SecretKindEnum.Password && pg.SecretSlots[1].AcceptedKinds.Single() == SecretKindEnum.ConnectionString, "slot kinds");
                ConnectorDescriptor ms = new SqlServerConnector().Descriptor;
                SqlTestSupport.Check(ms.SettingsSchema.Find("trustServerCertificate") != null && ms.SettingsSchema.Find("port")!.DefaultValue == "1433", "sql server settings");
                ConnectorDescriptor my = new MysqlConnector().Descriptor;
                SqlTestSupport.Check(my.SettingsSchema.Find("port")!.DefaultValue == "3306" && my.SettingsSchema.Find("trustServerCertificate") == null, "mysql settings");
                ConnectorDescriptor lite = new SqliteConnector().Descriptor;
                SqlTestSupport.Check(lite.SettingsSchema.Find("filename") != null && lite.SettingsSchema.Find("host") == null, "sqlite settings");
                SqlTestSupport.Check(lite.SecretSlots.Select(s => s.Name).SequenceEqual(new[] { "connectionString" }), "sqlite slots");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "validation", "ValidateAsync enforces host or connection string, port range, and enforcement values", async ct =>
            {
                PostgresqlConnector pg = new PostgresqlConnector();
                ConnectorValidationResult missing = await pg.ValidateAsync(SqlTestSupport.Resource("postgresql", new Dictionary<string, string>()), ct).ConfigureAwait(false);
                SqlTestSupport.Check(!missing.IsValid && missing.Errors.Any(e => e.Field == "host" && e.Code == "required"), "host required");
                ConnectorValidationResult viaSecret = await pg.ValidateAsync(SqlTestSupport.Resource("postgresql", new Dictionary<string, string>(), Secret("connectionString", "connectionString", "Host=db;Database=x;Username=u")), ct).ConfigureAwait(false);
                SqlTestSupport.Check(viaSecret.IsValid, "connection string satisfies host");
                ConnectorValidationResult badPort = await pg.ValidateAsync(SqlTestSupport.Resource("postgresql", new Dictionary<string, string> { { "host", "db" }, { "port", "70000" }, { "readOnlyEnforcement", "Sometimes" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Check(badPort.Errors.Any(e => e.Field == "port") && badPort.Errors.Any(e => e.Field == "readOnlyEnforcement"), "port and enforcement errors");
                ConnectorValidationResult badCs = await pg.ValidateAsync(SqlTestSupport.Resource("postgresql", new Dictionary<string, string>(), Secret("connectionString", "connectionString", "NotAKey=1;Password=hunter2")), ct).ConfigureAwait(false);
                SqlTestSupport.Check(badCs.Errors.Any(e => e.Field == "connectionString" && !e.Message.Contains("hunter2", StringComparison.Ordinal)), "invalid connection string without echoing secrets");
                SqliteConnector lite = new SqliteConnector();
                ConnectorValidationResult noFile = await lite.ValidateAsync(SqlTestSupport.Resource("sqlite", new Dictionary<string, string>()), ct).ConfigureAwait(false);
                SqlTestSupport.Check(noFile.Errors.Any(e => e.Field == "filename"), "sqlite filename required");
                ConnectorValidationResult file = await lite.ValidateAsync(SqlTestSupport.Resource("sqlite", new Dictionary<string, string> { { "filename", "/tmp/x.db" } }), ct).ConfigureAwait(false);
                SqlTestSupport.Check(file.IsValid, "sqlite valid");
            }));

            cases.Add(new TestCaseDescriptor(suite, "connection-strings", "Connection strings carry TLS, application name, timeouts, and read-only mode", ct =>
            {
                SqlResourceOptions o = SqlResourceOptions.FromResource(SqlTestSupport.Resource("postgresql",
                    new Dictionary<string, string> { { "host", "db.internal" }, { "port", "6543" }, { "database", "shop" }, { "username", "setting-user" }, { "sslMode", "Require" }, { "commandTimeoutSeconds", "12" } },
                    Secret("password", "password", "pw", "username", "secret-user")));
                SqlTestSupport.Check(o.Username == "secret-user", "secret username overrides the setting");
                string pg = new PostgresqlDialect().BuildConnectionString(o, false);
                SqlTestSupport.Check(pg.Contains("Host=db.internal", StringComparison.Ordinal) && pg.Contains("Port=6543", StringComparison.Ordinal), "pg host and port: " + Redact(pg));
                SqlTestSupport.Check(pg.Contains("Application Name=hugo", StringComparison.Ordinal) && pg.Contains("SSL Mode=Require", StringComparison.Ordinal) && pg.Contains("Command Timeout=12", StringComparison.Ordinal), "pg options: " + Redact(pg));
                string my = new MysqlDialect().BuildConnectionString(o, false);
                SqlTestSupport.Check(my.Contains("Port=6543", StringComparison.Ordinal) && my.Contains("SSL Mode=Required", StringComparison.OrdinalIgnoreCase), "mysql options: " + Redact(my));
                string ms = new SqlServerDialect().BuildConnectionString(o, false);
                SqlTestSupport.Check(ms.Contains("db.internal,6543", StringComparison.Ordinal) && ms.Contains("Application Name=hugo", StringComparison.Ordinal) && ms.Contains("Encrypt=True", StringComparison.Ordinal), "sql server options: " + Redact(ms));

                SqlResourceOptions viaSecret = SqlResourceOptions.FromResource(SqlTestSupport.Resource("postgresql",
                    new Dictionary<string, string> { { "host", "ignored-host" } },
                    Secret("connectionString", "connectionString", "Host=from-secret;Database=x;Application Name=custom")));
                string pgSecret = new PostgresqlDialect().BuildConnectionString(viaSecret, false);
                SqlTestSupport.Check(pgSecret.Contains("Host=from-secret", StringComparison.Ordinal) && !pgSecret.Contains("ignored-host", StringComparison.Ordinal), "connection string overrides discrete fields");
                SqlTestSupport.Check(pgSecret.Contains("Application Name=custom", StringComparison.Ordinal), "connection string application name kept");

                SqlResourceOptions lite = SqlResourceOptions.FromResource(SqlTestSupport.Resource("sqlite", new Dictionary<string, string> { { "filename", "/data/shop.db" } }));
                SqliteDialect sqlite = new SqliteDialect();
                SqlTestSupport.Check(sqlite.BuildConnectionString(lite, true).Contains("Mode=ReadOnly", StringComparison.Ordinal), "sqlite read-only mode");
                SqlTestSupport.Check(sqlite.BuildConnectionString(lite, false).Contains("Mode=ReadWrite", StringComparison.Ordinal), "sqlite read-write mode never creates files");
                bool threw = false;
                try
                {
                    sqlite.BuildConnectionString(new SqlResourceOptions(), false);
                }
                catch (ConnectorException e) when (e.Code == "setting_missing")
                {
                    threw = true;
                }
                SqlTestSupport.Check(threw, "missing filename");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "options-clamping", "Resource options clamp ranges and parse enforcement", ct =>
            {
                SqlResourceOptions o = SqlResourceOptions.FromResource(SqlTestSupport.Resource("mysql", new Dictionary<string, string>
                {
                    { "commandTimeoutSeconds", "99999" }, { "maxRows", "0" }, { "schemaCacheMinutes", "-5" }, { "readOnlyEnforcement", "classifieronly" }, { "allowMultipleStatements", "true" }
                }));
                SqlTestSupport.Check(o.CommandTimeoutSeconds == 3600 && o.MaxRows == 1 && o.SchemaCacheMinutes == 0, "clamped");
                SqlTestSupport.Check(o.ReadOnlyEnforcement == ReadOnlyEnforcementEnum.ClassifierOnly && o.AllowMultipleStatements, "enforcement and batches");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "parameters", "Parameters bind typed scalars and reject malformed input", ct =>
            {
                List<SqlParameterValue> p = SqlParameterParser.Parse("{\"@id\":5,\"name\":\"x\",\"ok\":true,\"n\":null,\"price\":9.95,\"tags\":[1,2]}");
                SqlTestSupport.Check(p.Count == 6 && p[0].Name == "id" && p[0].Value is long l && l == 5, "long and prefix stripped");
                SqlTestSupport.Check(p[1].Value is string && p[2].Value is bool && p[3].Value == null && p[4].Value is decimal && p[5].Value is string, "types");
                SqlTestSupport.Check(SqlParameterParser.Parse(null).Count == 0, "none");
                foreach (string bad in new[] { "[1]", "{\"bad name\":1}", "{nope" })
                {
                    bool threw = false;
                    try
                    {
                        SqlParameterParser.Parse(bad);
                    }
                    catch (ConnectorException e) when (e.Code == "invalid_argument")
                    {
                        threw = true;
                    }
                    SqlTestSupport.Check(threw, "rejects " + bad);
                }
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "value-formatting", "Values render as invariant text", ct =>
            {
                SqlTestSupport.Check(SqlValueFormatter.Format(DBNull.Value) == null, "null");
                SqlTestSupport.Check(SqlValueFormatter.Format(12.5m) == "12.5", "decimal");
                SqlTestSupport.Check(SqlValueFormatter.Format(true) == "true", "bool");
                SqlTestSupport.Check(SqlValueFormatter.Format(new byte[] { 1, 255 }) == "0x01ff", "binary");
                SqlTestSupport.Check(SqlValueFormatter.Format(new DateTime(2025, 6, 1, 10, 0, 0)) == "2025-06-01 10:00:00", "datetime");
                SqlTestSupport.Check(SqlValueFormatter.Format(new DateTime(2025, 6, 1)) == "2025-06-01", "date");
                SqlTestSupport.Check(SqlValueFormatter.Format(new[] { 1, 2 }) == "{1,2}", "array");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "tool-classify", "sql_query classifies per call and rejects batches through the tool", ct =>
            {
                PostgresqlConnector pg = new PostgresqlConnector();
                IConnectorTool query = SqlTestSupport.Tool(pg, "sql_query");
                ConnectorInvocationContext context = SqlTestSupport.Context(SqlTestSupport.Resource("postgresql", new Dictionary<string, string> { { "host", "db" } }), AccessModeEnum.ReadWriteWithApproval);
                EffectClassification read = query.Classify(context, new ToolArguments("{\"sql\":\"SELECT 1\"}"));
                SqlTestSupport.Check(read.Effect == EffectEnum.Read && !read.Rejected, "read");
                EffectClassification del = query.Classify(context, new ToolArguments("{\"sql\":\"DELETE FROM orders WHERE order_id = 1\"}"));
                SqlTestSupport.Check(del.Effect == EffectEnum.Destructive && del.ApprovalSummary.Contains("DELETE FROM orders", StringComparison.Ordinal) && del.Reason.Contains("DELETE", StringComparison.Ordinal), "destructive with summary");
                EffectClassification batch = query.Classify(context, new ToolArguments("{\"sql\":\"SELECT 1; SELECT 2\"}"));
                SqlTestSupport.Check(batch.Rejected, "batch rejected");
                ConnectorInvocationContext allow = SqlTestSupport.Context(SqlTestSupport.Resource("postgresql", new Dictionary<string, string> { { "host", "db" }, { "allowMultipleStatements", "true" } }), AccessModeEnum.ReadWrite);
                EffectClassification allowed = query.Classify(allow, new ToolArguments("{\"sql\":\"SELECT 1; UPDATE t SET a = 1\"}"));
                SqlTestSupport.Check(!allowed.Rejected && allowed.Effect == EffectEnum.Write, "batch allowed as most severe");
                EffectClassification missing = query.Classify(context, new ToolArguments("{}"));
                SqlTestSupport.Check(missing.Effect == EffectEnum.Destructive, "missing sql fails closed");
                EffectClassification explain = SqlTestSupport.Tool(pg, "sql_explain").Classify(context, new ToolArguments("{\"sql\":\"DELETE FROM t\"}"));
                SqlTestSupport.Check(explain.Effect == EffectEnum.Read, "sql_explain is Read");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "schema-cache", "Schema cache honors TTL, invalidation, and capacity", ct =>
            {
                SchemaCache cache = new SchemaCache { MaxEntries = 2 };
                cache.Set("a", new SchemaDetail { Schema = "a" }, TimeSpan.FromMinutes(5));
                SqlTestSupport.Check(cache.Get("a")?.Detail.Schema == "a", "hit");
                cache.Set("expired", new SchemaDetail(), TimeSpan.FromMilliseconds(-1));
                SqlTestSupport.Check(cache.Get("expired") == null, "zero ttl stores nothing");
                cache.Set("b", new SchemaDetail(), TimeSpan.FromMinutes(10));
                cache.Set("c", new SchemaDetail(), TimeSpan.FromMinutes(20));
                SqlTestSupport.Check(cache.Count == 2 && cache.Get("a") == null, "evicts soonest expiry");
                cache.Invalidate("b");
                SqlTestSupport.Check(cache.Get("b") == null, "invalidate");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suite, "SQL connectors (unit)", cases);
        }

        /// <summary>
        /// One instance of each SQL connector in a stable order.
        /// </summary>
        /// <returns>Connectors.</returns>
        public static List<SqlConnectorBase> AllConnectors()
        {
            return new List<SqlConnectorBase> { new SqliteConnector(), new MysqlConnector(), new SqlServerConnector(), new PostgresqlConnector() };
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, Dictionary<string, string>> Secret(string slot, string field, string value, string? field2 = null, string? value2 = null)
        {
            Dictionary<string, string> fields = new Dictionary<string, string> { { field, value } };
            if (field2 != null && value2 != null) fields[field2] = value2;
            return new Dictionary<string, Dictionary<string, string>> { { slot, fields } };
        }

        private static string Redact(string connectionString)
        {
            return System.Text.RegularExpressions.Regex.Replace(connectionString, "Password=[^;]*", "Password=***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        #endregion
    }
}
