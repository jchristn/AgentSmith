namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Hugo.Connectors.Sql.Intelligence;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using Touchstone.Core;

    /// <summary>
    /// Unit suite for the database intelligence logic that needs no database: name heuristics, relationship inference
    /// on synthetic schemas, the knowledge content guard, model output parsing, sample masking, and paging.
    /// </summary>
    public static class SqlIntelligenceUnitSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            const string suite = "connectors.sql.intelligence-unit";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(suite, "name-heuristics", "Key, metric, status, freshness, and type heuristics", ct =>
            {
                foreach (string yes in new[] { "customer_id", "customerId", "CustomerID", "order_ID", "legacy_account_id" })
                    SqlTestSupport.Check(NameHeuristics.IsLikelyForeignKey(yes), yes + " is key-like");
                foreach (string no in new[] { "id", "ID", "paid", "uuid", "valid", "guid", "rapid", "squid" })
                    SqlTestSupport.Check(!NameHeuristics.IsLikelyForeignKey(no), no + " is not key-like");
                SqlTestSupport.Check(NameHeuristics.ForeignKeyEntityToken("customer_id") == "customer" && NameHeuristics.ForeignKeyEntityToken("legacyAccountId") == "legacyaccount", "entity tokens");
                SqlTestSupport.Check(NameHeuristics.Singularize("categories") == "category" && NameHeuristics.Singularize("addresses") == "address" && NameHeuristics.Singularize("orders") == "order" && NameHeuristics.Singularize("class") == "class", "singular");
                SqlTestSupport.Check(NameHeuristics.Words("legacyAccounts").SequenceEqual(new[] { "legacy", "accounts" }) && NameHeuristics.Words("order_items").SequenceEqual(new[] { "order", "items" }), "words");
                SqlTestSupport.Check(NameHeuristics.TypeFamily("NUMERIC(10,2)") == "number" && NameHeuristics.TypeFamily("nvarchar(20)") == "text" && NameHeuristics.TypeFamily("timestamp without time zone") == "date" && NameHeuristics.TypeFamily("uniqueidentifier") == "uuid" && NameHeuristics.TypeFamily(String.Empty) == String.Empty, "type families");
                SqlTestSupport.Check(!NameHeuristics.TypesCompatible("integer", "varchar(20)") && NameHeuristics.TypesCompatible("uuid", "text") && NameHeuristics.TypesCompatible("int", "bigint") && NameHeuristics.TypesCompatible(null, "int"), "type compatibility");
                SqlTestSupport.Check(NameHeuristics.IsFreshnessColumn("created_at", "TEXT") && NameHeuristics.IsFreshnessColumn("ordered_at", "timestamp") && NameHeuristics.IsFreshnessColumn("event_date", "date"), "freshness");
                SqlTestSupport.Check(!NameHeuristics.IsFreshnessColumn("time_zone", "TEXT") && !NameHeuristics.IsFreshnessColumn("deleted_at", "timestamp") && !NameHeuristics.IsFreshnessColumn("ordered_at", "integer") && !NameHeuristics.IsFreshnessColumn("lifetime_total", "numeric"), "not freshness");
                SqlTestSupport.Check(NameHeuristics.IsStatusColumn("order_status") && NameHeuristics.IsStatusColumn("orderstatus") && NameHeuristics.IsStatusColumn("State") && !NameHeuristics.IsStatusColumn("statement"), "status words");
                SqlTestSupport.Check(NameHeuristics.IsMetricColumn("total", "numeric") && !NameHeuristics.IsMetricColumn("total", "varchar(10)") && !NameHeuristics.IsMetricColumn("price_id", "int"), "metrics");
                SqlTestSupport.Check(NameHeuristics.IsActivityColumn("is_active") && NameHeuristics.IsSoftDeleteColumn("is_deleted") && NameHeuristics.IsTenantColumn("tenant_id") && NameHeuristics.IsOwnerColumn("created_by") && NameHeuristics.IsPartyTable("app_users"), "flags");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "inference-synthetic", "Inference handles camel case, prefixes, types, uniqueness, composite keys, and declared keys", ct =>
            {
                SchemaDetail detail = new SchemaDetail { Engine = "test", Schema = "app" };
                detail.Tables.Add(Table("users", Col("id", "integer", true), Col("email", "text")));
                detail.Tables.Add(Table("posts", Col("id", "integer", true), Col("userId", "integer"), Col("author_id", "integer")));
                detail.Tables.Add(Table("teams", Col("team_id", "integer", true)));
                detail.Tables.Add(Table("members", Col("member_id", "integer", true), Col("team_id", "varchar(20)")));
                detail.Tables.Add(Table("memberships", Col("team_id", "integer", true), Col("user_id", "integer", true)));
                detail.Tables.Add(Table("events", Col("event_id", "integer", true), Col("membership_id", "integer")));
                detail.Tables.Add(Table("tags", Col("id", "integer"), Col("label", "text")));
                detail.Tables.Add(Table("post_tags", Col("post_id", "integer"), Col("tag_id", "integer")));
                TableDetail comments = Table("comments", Col("comment_id", "integer", true), Col("post_id", "integer"));
                comments.ForeignKeys.Add(new ForeignKeyDetail { ConstraintName = "fk_comments_post", ColumnName = "post_id", ReferencedTable = "posts", ReferencedColumn = null });
                detail.Tables.Add(comments);

                List<RelationshipDetail> edges = DatabaseIntelligenceBuilder.BuildRelationships(detail, true);
                RelationshipDetail camel = edges.Single(e => e.FromTable == "posts" && e.FromColumn == "userId");
                SqlTestSupport.Check(camel.ToTable == "users" && camel.ToColumn == "id" && camel.Confidence == 0.9, "camel case: " + camel.Confidence);
                SqlTestSupport.Check(!edges.Any(e => e.FromColumn == "author_id"), "no table named author");
                RelationshipDetail typed = edges.Single(e => e.FromTable == "members" && e.FromColumn == "team_id");
                SqlTestSupport.Check(typed.Confidence < 0.7 && typed.Evidence.Any(x => x.StartsWith("Types differ", StringComparison.Ordinal)), "type mismatch lowers confidence: " + typed.Confidence);
                SqlTestSupport.Check(!edges.Any(e => e.FromTable == "events" && e.FromColumn == "membership_id"), "composite primary key is not guessed");
                RelationshipDetail weak = edges.Single(e => e.FromTable == "post_tags" && e.FromColumn == "tag_id");
                SqlTestSupport.Check(weak.ToTable == "tags" && weak.Evidence.Any(x => x.Contains("not declared unique", StringComparison.Ordinal)) && weak.Confidence < 0.8, "non-unique target: " + weak.Confidence);
                RelationshipDetail declared = edges.Single(e => e.FromTable == "comments" && e.FromColumn == "post_id");
                SqlTestSupport.Check(declared.IsDeclared && declared.ToColumn == "id" && declared.ToSchema == "app", "implicit referenced column resolved to the primary key");
                SqlTestSupport.Check(edges.Count(e => e.FromTable == "comments") == 1, "declared key suppresses inference from the same column");
                SqlTestSupport.Check(edges.Where(e => !e.IsDeclared).All(e => e.Confidence <= 0.95 && e.Evidence.Count >= 2), "inferred edges carry evidence and stay below 1.0");

                DatabaseIntelligence intel = DatabaseIntelligenceBuilder.Build(detail, null);
                SqlTestSupport.Check(intel.Domain.Entities.Single(e => e.TableName == "post_tags").Role == "junction", "junction role");
                SqlTestSupport.Check(intel.ContextQuality.Signals.Any(s => s.Key == "database_context") && intel.Ambiguities.Any(a => a.Term == "business_context"), "gaps without context");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "content-guard", "The knowledge guard refuses secrets, credentials, rows, and personal data", ct =>
            {
                KnowledgeContentGuard guard = new KnowledgeContentGuard();
                List<string> secrets = new List<string> { "Sup3rS3cret!" };
                string[] refused =
                {
                    "Login with Sup3rS3cret! as the password.",
                    "password: hunter2hunter2",
                    "api_key=abcd1234efgh",
                    "-----BEGIN OPENSSH PRIVATE KEY-----",
                    "AWS key AKIAABCDEFGHIJKLMNOP",
                    "Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345",
                    "token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U",
                    "Server=db;User Id=sa;Password=x;",
                    "postgres://app:pw123@db.internal/shop",
                    "a@x.com b@x.com c@x.com",
                    String.Join("\n", Enumerable.Range(1, 13).Select(i => "| " + i + " | x | y |")),
                    String.Join("\n", Enumerable.Range(1, 13).Select(i => i + "\tx\ty")),
                    String.Empty,
                    new string('a', 20000)
                };
                foreach (string text in refused) SqlTestSupport.Check(guard.Check(text, secrets) != null, "refused: " + (text.Length > 60 ? text.Substring(0, 60) : text));
                string[] accepted =
                {
                    "Revenue means SUM(orders.total) for orders that are not cancelled.",
                    "legacy_invoices.account_id joins legacy_accounts.account_id (inferred, confirmed by values).",
                    "The token column stores hashed session tokens; never select it.",
                    "Support contact is support@example.com.",
                    "| status | meaning |\n|---|---|\n| open | billable |\n| void | ignore |"
                };
                foreach (string text in accepted) SqlTestSupport.Check(guard.Check(text, secrets) == null, "accepted: " + text + " -> " + guard.Check(text, secrets));
                SqlTestSupport.Check(!guard.Check("Login with Sup3rS3cret! as the password.", secrets)!.Contains("Sup3rS3cret!", StringComparison.Ordinal), "refusal never echoes the secret");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "model-text", "Model output parsing: JSON plans, generated context, and SQL", ct =>
            {
                QueryPlan? fenced = ModelText.ParseJson<QueryPlan>("Here is the plan:\n```json\n{\"Execute\":true,\"SQL\":\"SELECT 1\",\"Tables\":[\"t\"]}\n```\nDone.");
                SqlTestSupport.Check(fenced != null && fenced.Execute && fenced.Sql == "SELECT 1" && fenced.Tables!.Single() == "t", "fenced, case-insensitive");
                QueryPlan? thinking = ModelText.ParseJson<QueryPlan>("<think>{\"execute\":false}</think>{\"execute\":true,\"sql\":\"SELECT 2\"}");
                SqlTestSupport.Check(thinking != null && thinking.Sql == "SELECT 2", "reasoning blocks ignored");
                SqlTestSupport.Check(ModelText.ParseJson<QueryPlan>("no json here") == null && ModelText.ParseJson<QueryPlan>("{not json}") == null, "invalid JSON is null");
                QueryPlan? clarify = ModelText.ParseJson<QueryPlan>("{\"execute\":false,\"ambiguous\":true,\"clarification\":\"Which date?\"}");
                SqlTestSupport.Check(clarify != null && clarify.Ambiguous && clarify.Clarification == "Which date?", "clarification plan");
                SqlTestSupport.Check(ModelText.NormalizeSql("```sql\nSELECT 1;\n```") == "SELECT 1" && ModelText.NormalizeSql("SELECT 1; SELECT 2;") == "SELECT 1; SELECT 2;", "SQL normalization keeps batches visible to the classifier");
                SqlTestSupport.Check(ModelText.NormalizeGeneratedContext("<think>x</think>```markdown\n# Shop\nText\n```") == "# Shop\nText", "context normalization");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "masking-and-paging", "Samples are masked and cursors are validated", ct =>
            {
                SampleMasker masker = new SampleMasker { MaxValueChars = 10 };
                SqlQueryResult sample = new SqlQueryResult();
                sample.Columns.Add(new SqlColumnInfo("email", "text"));
                sample.Columns.Add(new SqlColumnInfo("password_hash", "text"));
                sample.Columns.Add(new SqlColumnInfo("note", "text"));
                sample.Rows.Add(new List<string?> { "ada@example.com", "abc123", "a fairly long note value" });
                masker.Mask(sample);
                SqlTestSupport.Check(sample.Rows[0][0] == "(personal data)" && sample.Rows[0][1] == "(redacted)" && sample.Rows[0][2] == "a fairly l...", "masked: " + String.Join(" | ", sample.Rows[0]));

                SqlTestSupport.Check(SchemaProjection.ParseCursor(null) == 0 && SchemaProjection.ParseCursor("25") == 25, "cursor parse");
                bool threw = false;
                try
                {
                    SchemaProjection.ParseCursor("-1");
                }
                catch (ConnectorException e) when (e.Code == "invalid_argument")
                {
                    threw = true;
                }
                SqlTestSupport.Check(threw, "negative cursor rejected");

                SchemaDetail detail = new SchemaDetail { Schema = "s" };
                for (int i = 0; i < 5; i++) detail.Tables.Add(Table("t" + i, Col("id", "int", true)));
                SchemaPage<TableSummary> last = SchemaProjection.CreateTableListResult(detail, null, 2, "4");
                SqlTestSupport.Check(last.Items.Count == 1 && last.EndOfResults && last.NextCursor == null && last.Cursor == "4", "last page");
                SchemaPage<TableSummary> beyond = SchemaProjection.CreateTableListResult(detail, null, 2, "40");
                SqlTestSupport.Check(beyond.Items.Count == 0 && beyond.EndOfResults, "beyond the end");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "dialect-sample-sql", "Sample queries quote identifiers and bound rows per engine", ct =>
            {
                List<string> columns = new List<string> { "id", "we\"ird" };
                SqlTestSupport.Check(new SqliteDialect().BuildSampleSql("main", "orders", columns, 3) == "SELECT \"id\", \"we\"\"ird\" FROM \"main\".\"orders\" LIMIT 3", "sqlite");
                SqlTestSupport.Check(new PostgresqlDialect().BuildSampleSql("public", "orders", columns, 3).EndsWith("FROM \"public\".\"orders\" LIMIT 3", StringComparison.Ordinal), "postgresql");
                SqlTestSupport.Check(new MysqlDialect().BuildSampleSql("shop", "or`ders", new List<string> { "id" }, 2) == "SELECT `id` FROM `shop`.`or``ders` LIMIT 2", "mysql");
                SqlTestSupport.Check(new SqlServerDialect().BuildSampleSql("dbo", "or]ders", new List<string> { "id" }, 2) == "SELECT TOP (2) [id] FROM [dbo].[or]]ders]", "sqlserver");
                SqlTestSupport.Check(new SqlServerDialect().LimitSyntax == "TOP (n)" && new SqliteDialect().LimitSyntax == "LIMIT n", "limit syntax");
                foreach (SqlDialect d in new SqlDialect[] { new SqliteDialect(), new PostgresqlDialect(), new MysqlDialect(), new SqlServerDialect() })
                    SqlTestSupport.Check(StatementClassifier.Classify(d.BuildSampleSql("s", "t", new List<string> { "a" }, 3), d.Engine).Effect == EffectEnum.Read, d.DisplayName + " sample SQL classifies as Read");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suite, "SQL database intelligence logic without a database", cases);
        }

        #endregion

        #region Private-Methods

        private static TableDetail Table(string name, params ColumnDetail[] columns)
        {
            TableDetail t = new TableDetail { SchemaName = "app", TableName = name, TableType = "BASE TABLE", RowEstimate = 10 };
            int ordinal = 1;
            foreach (ColumnDetail c in columns)
            {
                c.Ordinal = ordinal++;
                t.Columns.Add(c);
            }
            List<string> pk = columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();
            if (pk.Count > 0) t.Indexes.Add(new IndexDetail { IndexName = "pk_" + name, Columns = pk, IsUnique = true, IsPrimary = true });
            return t;
        }

        private static ColumnDetail Col(string name, string type, bool primaryKey = false)
        {
            return new ColumnDetail { ColumnName = name, DataType = type, IsPrimaryKey = primaryKey, IsNullable = !primaryKey };
        }

        #endregion
    }
}
