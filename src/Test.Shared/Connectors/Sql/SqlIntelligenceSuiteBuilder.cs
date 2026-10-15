namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Hugo.Connectors.Sql.Intelligence;
    using Hugo.Connectors.Sql.Tools;
    using Hugo.Connectors.Sql.Vendored.Tablix;
    using Touchstone.Core;

    /// <summary>
    /// Database intelligence cases every SQL engine runs against the seeded shop schema (which includes a legacy pair
    /// with no declared foreign keys): relationships, intelligence, paging, the agent pack, durable context through the
    /// knowledge store, context generation and sql_ask with a scripted model, ambiguity detection, and every
    /// verification status.
    /// </summary>
    public static class SqlIntelligenceSuiteBuilder
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
            string schema = target.DefaultSchema;

            void Add(string id, string name, Func<CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(suite, "intel-" + id, target.Type + ": " + name, body, null, skip, skipReason));
            }

            string Key(string table)
            {
                return TableIdentity.Key(schema, table);
            }

            ConnectorInvocationContext Ctx(IResourceKnowledgeStore? store = null, IConnectorModel? model = null, bool allowKnowledge = true, int maxRows = 500, Dictionary<string, Dictionary<string, string>>? extraSecrets = null)
            {
                Dictionary<string, string> settings = target.Settings();
                Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>(target.Secrets);
                if (extraSecrets != null)
                {
                    foreach (KeyValuePair<string, Dictionary<string, string>> kv in extraSecrets) secrets[kv.Key] = kv.Value;
                }
                ConnectorResourceContext resource = SqlTestSupport.Resource(target.Type, settings, secrets);
                resource.ConfigurationHash = target.Type + "-intel-" + String.Join(",", settings.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value));
                resource.Knowledge = store ?? new InMemoryResourceKnowledgeStore();
                ConnectorInvocationContext context = SqlTestSupport.Context(resource, AccessModeEnum.ReadOnly, 60, maxRows);
                context.Model = model;
                context.AllowKnowledgeUpdates = allowKnowledge;
                context.PrincipalSummary = "user anderson@hugo.local via test";
                return context;
            }

            Task<ToolResult> Run(string tool, string json, ConnectorInvocationContext context, CancellationToken token)
            {
                return SqlTestSupport.RunAsync(connector, tool, json, context, token);
            }

            async Task<SchemaDetail> CrawlAsync(CancellationToken ct)
            {
                ToolResult r = await Run("sql_describe_schema", "{}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "crawl");
                return SqlJson.Deserialize<SchemaDetail>(r.StructuredJson!)!;
            }

            Add("relationships", "sql_list_relationships returns declared keys and inferred candidates with confidence and evidence", async ct =>
            {
                ToolResult r = await Run("sql_list_relationships", "{\"refresh\":true}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "relationships");
                SchemaPage<RelationshipDetail> page = Page<RelationshipDetail>(r);
                List<RelationshipDetail> declared = page.Items.Where(i => i.IsDeclared).ToList();
                SqlTestSupport.Check(declared.Count == 3, "three declared keys: " + r.Text);
                SqlTestSupport.Check(declared.Any(d => d.FromTable == "orders" && d.FromColumn == "customer_id" && d.ToTable == "customers" && d.ToColumn == "customer_id" && d.Confidence == 1.0), "orders -> customers declared");
                SqlTestSupport.Check(declared.Any(d => d.FromTable == "order_items" && d.FromColumn == "product_id" && d.ToTable == "products"), "order_items -> products declared");

                RelationshipDetail? legacy = page.Items.SingleOrDefault(i => i.FromTable == "legacy_invoices" && i.FromColumn == "account_id");
                SqlTestSupport.Check(legacy != null && !legacy.IsDeclared && legacy.Source == RelationshipDetail.InferredSource, "legacy join inferred without a declared key: " + r.Text);
                SqlTestSupport.Check(legacy!.ToTable == "legacy_accounts" && legacy.ToColumn == "account_id" && legacy.Confidence >= 0.8 && legacy.Confidence < 1.0, "legacy target and confidence " + legacy.Confidence);
                SqlTestSupport.Check(legacy.Evidence.Any(e => e.Contains("prefix", StringComparison.Ordinal)) && legacy.Evidence.Any(e => e.Contains("primary key", StringComparison.Ordinal)) && legacy.Evidence.Any(e => e.Contains("indexed", StringComparison.Ordinal)), "evidence: " + String.Join(" | ", legacy.Evidence));
                SqlTestSupport.Check(page.Items.Any(i => i.FromTable == "customer_order_totals" && i.FromColumn == "customer_id" && i.ToTable == "customers" && !i.IsDeclared), "view joins inferred to customers");
                SqlTestSupport.Check(!page.Items.Any(i => !i.IsDeclared && i.FromTable == "orders" && i.FromColumn == "customer_id"), "declared keys are not duplicated as inferred");
                SqlTestSupport.Check(!page.Items.Any(i => i.ToTable == "customer_order_totals"), "views are never inferred targets");
                SqlTestSupport.Check(page.Items.Count == 5, "five edges in all: " + r.Text);
                SqlTestSupport.Check(r.Text.Contains("legacy_invoices.account_id -> " + Key("legacy_accounts") + ".account_id | inferred", StringComparison.OrdinalIgnoreCase), "one line per edge: " + r.Text);

                ToolResult byTable = await Run("sql_list_relationships", "{\"table\":\"legacy_invoices\"}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Page<RelationshipDetail>(byTable).Items.Count == 1, "table filter");
                ToolResult byQualified = await Run("sql_list_relationships", "{\"table\":" + SqlTestSupport.Json(Key("customers")) + "}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Page<RelationshipDetail>(byQualified).Items.Count == 2, "qualified table filter matches either end: " + byQualified.Text);
                ToolResult declaredOnly = await Run("sql_list_relationships", "{\"includeInferred\":false}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Page<RelationshipDetail>(declaredOnly).Items.All(i => i.IsDeclared) && Page<RelationshipDetail>(declaredOnly).TotalRecords == 3, "declared only");
                ToolResult confident = await Run("sql_list_relationships", "{\"minConfidence\":0.95}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Check(Page<RelationshipDetail>(confident).TotalRecords == 3, "min confidence");

                List<RelationshipDetail> walked = new List<RelationshipDetail>();
                string? cursor = null;
                int pages = 0;
                do
                {
                    string json = "{\"pageSize\":2" + (cursor == null ? String.Empty : ",\"cursor\":" + SqlTestSupport.Json(cursor)) + "}";
                    SchemaPage<RelationshipDetail> p = Page<RelationshipDetail>(await Run("sql_list_relationships", json, Ctx(), ct).ConfigureAwait(false));
                    walked.AddRange(p.Items);
                    cursor = p.NextCursor;
                    pages++;
                }
                while (cursor != null && pages < 10);
                SqlTestSupport.Check(pages == 3 && walked.Count == 5, "paged walk: " + pages + " pages, " + walked.Count + " edges");

                SqlTestSupport.Error(await Run("sql_list_relationships", "{\"table\":\"no_such_table\"}", Ctx(), ct).ConfigureAwait(false), "not_found", "unknown table");
                SqlTestSupport.Error(await Run("sql_list_relationships", "{\"minConfidence\":2}", Ctx(), ct).ConfigureAwait(false), "invalid_argument", "bad confidence");
            });

            Add("intelligence", "sql_get_intelligence derives entities, metrics, filters, freshness, ambiguity, and context quality", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                ToolResult r = await Run("sql_get_intelligence", "{}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "intelligence");
                DatabaseIntelligence intel = SqlJson.Deserialize<DatabaseIntelligence>(r.StructuredJson!)!;
                SqlTestSupport.Check(intel.Domain.Entities.Count == 7 && intel.Engine == target.Type, "entities");
                SqlTestSupport.Check(Entity(intel, "customers").Role == "core entity" && Entity(intel, "products").Role == "core entity" && Entity(intel, "legacy_accounts").Role == "core entity", "core entities");
                SqlTestSupport.Check(Entity(intel, "customer_order_totals").Role == "view", "view role");
                SqlTestSupport.Check(intel.Domain.Metrics.Contains(Key("orders") + ".total") && intel.Domain.Metrics.Contains(Key("legacy_invoices") + ".amount") && intel.Domain.Metrics.Contains(Key("order_items") + ".quantity"), "metrics: " + String.Join(", ", intel.Domain.Metrics));
                SqlTestSupport.Check(!intel.Domain.Metrics.Any(m => m.EndsWith("_id", StringComparison.Ordinal)), "keys are not metrics");
                MetricCandidate unitPrice = intel.Domain.MetricDetails.Single(m => m.Column == Key("order_items") + ".unit_price");
                SqlTestSupport.Check(unitPrice.Aggregations.Contains("AVG") && !unitPrice.Aggregations.Contains("SUM") && unitPrice.Note != null, "unit price aggregates by average");
                SqlTestSupport.Check(intel.Domain.FreshnessColumns.Contains(Key("orders") + ".ordered_at") && intel.Domain.FreshnessColumns.Contains(Key("customers") + ".created_at"), "freshness: " + String.Join(", ", intel.Domain.FreshnessColumns));
                SqlTestSupport.Check(!intel.Domain.FreshnessColumns.Contains(Key("legacy_accounts") + ".deleted_at") && intel.Domain.SoftDeleteColumns.Contains(Key("legacy_accounts") + ".deleted_at"), "soft delete is not freshness");
                SqlTestSupport.Check(intel.Domain.CommonFilters.Contains(Key("orders") + ".status") && intel.Domain.CommonFilters.Contains(Key("products") + ".category") && intel.Domain.CommonFilters.Contains(Key("legacy_accounts") + ".is_active"), "filters: " + String.Join(", ", intel.Domain.CommonFilters));
                List<string> terms = intel.Ambiguities.Select(a => a.Term).ToList();
                foreach (string term in new[] { "latest", "active", "status", "revenue", "owner", "customer", "business_context" })
                    SqlTestSupport.Check(terms.Contains(term), "ambiguity " + term + " in " + String.Join(", ", terms));
                SqlTestSupport.Check(intel.Ambiguities.Single(a => a.Term == "customer").Candidates.Count == 2, "views are not party tables");
                ContextQualityScore before = intel.ContextQuality;
                SqlTestSupport.Check(!before.HasDatabaseContext && before.TablesWithContext == 0 && before.DeclaredRelationships == 3 && before.InferredRelationships == 2, "quality counts");
                SqlTestSupport.Check(before.Signals.Any(s => s.Key == "database_context") && before.Signals.Any(s => s.Key == "ambiguity"), "quality gaps");
                SqlTestSupport.Check(intel.SuggestedQuestions.Count > 0, "starter questions");
                SqlTestSupport.Check(r.Text.Contains("Context quality: ", StringComparison.Ordinal) && r.Text.Contains("Ambiguities to clarify", StringComparison.Ordinal), "text readout");

                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Database, Key = String.Empty, Content = "Shop database. Revenue means SUM(orders.total) excluding cancelled orders.", Source = KnowledgeSourceEnum.Operator }, ct).ConfigureAwait(false);
                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Table, Key = Key("orders"), Content = "One row per order.", Source = KnowledgeSourceEnum.Generated }, ct).ConfigureAwait(false);
                DatabaseIntelligence after = SqlJson.Deserialize<DatabaseIntelligence>((await Run("sql_get_intelligence", "{}", Ctx(store), ct).ConfigureAwait(false)).StructuredJson!)!;
                SqlTestSupport.Check(after.ContextQuality.HasDatabaseContext && after.ContextQuality.TablesWithContext == 1 && after.ContextQuality.TablesWithGeneratedContext == 1, "context counted");
                SqlTestSupport.Check(after.ContextQuality.Score > before.Score, "score rises with context: " + before.Score + " -> " + after.ContextQuality.Score);
                SqlTestSupport.Check(!after.Ambiguities.Any(a => a.Term == "revenue" || a.Term == "business_context"), "context definitions resolve ambiguity");
                SqlTestSupport.Check(Entity(after, "orders").HasContext && Entity(after, "orders").ContextSource == "Generated", "entity context marker");
            });

            Add("list-tables-paging", "sql_list_tables pages with a cursor, searches names and columns, and marks context", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                List<TableSummary> all = new List<TableSummary>();
                string? cursor = null;
                int pages = 0;
                do
                {
                    string json = "{\"pageSize\":3" + (cursor == null ? String.Empty : ",\"cursor\":" + SqlTestSupport.Json(cursor)) + "}";
                    ToolResult r = await Run("sql_list_tables", json, Ctx(store), ct).ConfigureAwait(false);
                    SqlTestSupport.Ok(r, "page");
                    SchemaPage<TableSummary> page = Page<TableSummary>(r);
                    SqlTestSupport.Check(page.Items.Count <= 3 && page.TotalRecords == 7, "page bounds");
                    if (page.NextCursor != null) SqlTestSupport.Check(r.Text.Contains("cursor \"" + page.NextCursor + "\"", StringComparison.Ordinal), "next cursor in text");
                    all.AddRange(page.Items);
                    cursor = page.NextCursor;
                    pages++;
                }
                while (cursor != null && pages < 10);
                SqlTestSupport.Check(pages == 3 && all.Select(t => t.TableName).Distinct().Count() == 7, "three pages, seven tables");

                SchemaPage<TableSummary> legacy = Page<TableSummary>(await Run("sql_list_tables", "{\"search\":\"legacy\"}", Ctx(store), ct).ConfigureAwait(false));
                SqlTestSupport.Check(legacy.TotalRecords == 2, "search legacy");
                SchemaPage<TableSummary> email = Page<TableSummary>(await Run("sql_list_tables", "{\"search\":\"email\"}", Ctx(store), ct).ConfigureAwait(false));
                SqlTestSupport.Check(email.Items.Count == 1 && email.Items[0].TableName == "customers", "search matches columns");
                SqlTestSupport.Check(Page<TableSummary>(await Run("sql_list_tables", "{\"pageSize\":1000}", Ctx(store, null, true, 2), ct).ConfigureAwait(false)).Items.Count == 2, "page size capped by the row limit");
                SqlTestSupport.Error(await Run("sql_list_tables", "{\"cursor\":\"abc\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "bad cursor");

                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Table, Key = Key("orders"), Content = "One row per order.", Source = KnowledgeSourceEnum.Agent }, ct).ConfigureAwait(false);
                ToolResult marked = await Run("sql_list_tables", "{}", Ctx(store), ct).ConfigureAwait(false);
                TableSummary orders = Page<TableSummary>(marked).Items.Single(t => t.TableName == "orders");
                SqlTestSupport.Check(orders.HasContext && orders.ContextSource == "Agent" && marked.Text.Contains("context (Agent)", StringComparison.Ordinal), "context marker: " + marked.Text);
            });

            Add("agent-pack", "sql_get_agent_pack bundles instructions, context, entities, relationships, and gaps within caps", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                ToolResult bare = await Run("sql_get_agent_pack", "{}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(bare, "pack");
                SqlTestSupport.Check(bare.Text.StartsWith("# SQL agent pack", StringComparison.Ordinal) && bare.MimeType == "text/markdown", "markdown pack");
                foreach (string section in new[] { "## How to work", "## Database context", "None saved yet", "## Entities", "## Relationships", "inferred", "## Likely metrics", "## Ambiguities to clarify", "## Context gaps", "sql_update_context", "sql_ask", connector.Dialect.LimitSyntax })
                    SqlTestSupport.Check(bare.Text.Contains(section, StringComparison.Ordinal), "pack contains " + section);

                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Database, Key = String.Empty, Content = "Shop database used by the storefront team.", Source = KnowledgeSourceEnum.Operator }, ct).ConfigureAwait(false);
                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Table, Key = Key("legacy_invoices"), Content = "Invoices from the old billing system. " + new string('x', 600), Source = KnowledgeSourceEnum.Agent }, ct).ConfigureAwait(false);
                ToolResult full = await Run("sql_get_agent_pack", "{\"maxEntities\":2}", Ctx(store), ct).ConfigureAwait(false);
                AgentPack pack = SqlJson.Deserialize<AgentPack>(full.StructuredJson!)!;
                SqlTestSupport.Check(pack.DatabaseContext == "Shop database used by the storefront team." && pack.DatabaseContextSource == "Operator", "database context included");
                SqlTestSupport.Check(pack.TableContexts.Count == 1 && pack.TableContexts[0].Table == Key("legacy_invoices") && pack.TableContexts[0].Shortened && pack.TableContexts[0].Content.Length < 450, "table context summarized and shortened");
                SqlTestSupport.Check(pack.Domain.Entities.Count == 2 && pack.OmittedEntities == 5 && full.Text.Contains("5 more; page with sql_list_tables", StringComparison.Ordinal), "entity cap");
                SqlTestSupport.Check(pack.Relationships.Take(3).All(r => r.IsDeclared) && pack.Relationships.Count == 5, "declared relationships first");
                SqlTestSupport.Check(full.Text.Contains("Shop database used by the storefront team.", StringComparison.Ordinal) && full.Text.Contains("### " + Key("legacy_invoices") + " (Agent)", StringComparison.Ordinal), "context rendered");
                SqlTestSupport.Check(pack.Instructions.Count >= 6 && pack.ContextQuality.HasDatabaseContext, "instructions and quality");
            });

            Add("context-round-trip", "sql_update_context and sql_get_context round-trip database and table context", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                ToolResult first = await Run("sql_update_context", "{\"content\":\"Shop database for the storefront.\",\"reason\":\"initial\"}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(first, "database update");
                ResourceKnowledgeEntry e1 = SqlJson.Deserialize<ResourceKnowledgeEntry>(first.StructuredJson!)!;
                SqlTestSupport.Check(e1.Scope == "database" && e1.Key == String.Empty && e1.Version == 1 && e1.Source == KnowledgeSourceEnum.Agent && e1.UpdatedBy.StartsWith("agent (", StringComparison.Ordinal), "stored as agent: " + e1.UpdatedBy);
                ToolResult second = await Run("sql_update_context", "{\"scope\":\"database\",\"content\":\"Cancelled orders are excluded from revenue.\"}", Ctx(store), ct).ConfigureAwait(false);
                ResourceKnowledgeEntry e2 = SqlJson.Deserialize<ResourceKnowledgeEntry>(second.StructuredJson!)!;
                SqlTestSupport.Check(e2.Version == 2 && e2.Content == "Shop database for the storefront.\n\nCancelled orders are excluded from revenue.", "append: " + e2.Content);

                ToolResult table = await Run("sql_update_context", "{\"table\":\"legacy_invoices\",\"content\":\"Joins legacy_accounts on account_id (inferred, confirmed by values).\"}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(table, "table update");
                SqlTestSupport.Check(SqlJson.Deserialize<ResourceKnowledgeEntry>(table.StructuredJson!)!.Key == Key("legacy_invoices"), "table key is schema.table");
                ToolResult replaced = await Run("sql_update_context", "{\"table\":" + SqlTestSupport.Json(Key("legacy_invoices")) + ",\"mode\":\"replace\",\"content\":\"Invoices from the old billing system.\"}", Ctx(store), ct).ConfigureAwait(false);
                ResourceKnowledgeEntry e3 = SqlJson.Deserialize<ResourceKnowledgeEntry>(replaced.StructuredJson!)!;
                SqlTestSupport.Check(e3.Content == "Invoices from the old billing system." && e3.Version == 2, "replace");

                ToolResult read = await Run("sql_get_context", "{\"table\":\"legacy_invoices\"}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(read, "read");
                ContextReadResult r = SqlJson.Deserialize<ContextReadResult>(read.StructuredJson!)!;
                SqlTestSupport.Check(r.Database != null && r.Database.Version == 2 && r.Tables.Count == 1 && r.Tables[0].Content == "Invoices from the old billing system.", "read back");
                SqlTestSupport.Check(read.Text.Contains("Cancelled orders are excluded from revenue.", StringComparison.Ordinal) && read.Text.Contains("(Agent, version 2", StringComparison.Ordinal), "text with provenance: " + read.Text);
                ToolResult empty = await Run("sql_get_context", "{\"table\":\"orders\"}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Check(SqlJson.Deserialize<ContextReadResult>(empty.StructuredJson!)!.TablesWithoutContext.SequenceEqual(new[] { Key("orders") }), "table without context");
                ContextReadResult listed = SqlJson.Deserialize<ContextReadResult>((await Run("sql_get_context", "{\"listTables\":true}", Ctx(store), ct).ConfigureAwait(false)).StructuredJson!)!;
                SqlTestSupport.Check(listed.TotalRecords == 1 && listed.Tables.Count == 1, "list tables with context");
                ContextReadResult withEmpty = SqlJson.Deserialize<ContextReadResult>((await Run("sql_get_context", "{\"listTables\":true,\"includeEmpty\":true,\"pageSize\":3}", Ctx(store), ct).ConfigureAwait(false)).StructuredJson!)!;
                SqlTestSupport.Check(withEmpty.TotalRecords == 7 && withEmpty.NextCursor == "3" && withEmpty.Tables.Count + withEmpty.TablesWithoutContext.Count == 3, "paged list with empty tables");

                SqlTestSupport.Error(await Run("sql_get_context", "{\"table\":\"nope\"}", Ctx(store), ct).ConfigureAwait(false), "not_found", "unknown table read");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"table\":\"nope\",\"content\":\"x\"}", Ctx(store), ct).ConfigureAwait(false), "not_found", "unknown table write");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"scope\":\"table\",\"content\":\"x\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "table scope without table");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":\"x\",\"mode\":\"merge\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "bad mode");
            });

            Add("context-refusals", "Knowledge updates are refused when the binding forbids them or content carries secrets, rows, or personal data; operator entries are protected", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                ToolResult denied = await Run("sql_update_context", "{\"content\":\"Shop database.\"}", Ctx(store, null, false), ct).ConfigureAwait(false);
                SqlTestSupport.Error(denied, "permission_denied", "AllowKnowledgeUpdates=false");
                SqlTestSupport.Check((await store.ListAsync(null, ct).ConfigureAwait(false)).Count == 0, "nothing stored when denied");

                Dictionary<string, Dictionary<string, string>> extra = new Dictionary<string, Dictionary<string, string>> { { "apiToken", new Dictionary<string, string> { { "value", "Zq8-unique-secret-77" } } } };
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":\"The reporting login uses Zq8-unique-secret-77 as its token.\"}", Ctx(store, null, true, 500, extra), ct).ConfigureAwait(false), "invalid_argument", "bound secret value");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":\"Connect with password = hunter2hunter2\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "password assignment");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":\"-----BEGIN RSA PRIVATE KEY-----\\nabc\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "private key");
                string rows = String.Join("\n", Enumerable.Range(1, 15).Select(i => "| " + i + " | customer " + i + " | 10.00 |"));
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":" + SqlTestSupport.Json("Recent orders:\n" + rows) + "}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "raw rows");
                SqlTestSupport.Error(await Run("sql_update_context", "{\"content\":\"Top buyers: ada.lovelace@example.com, grace.hopper@example.com, alan.turing@example.com\"}", Ctx(store), ct).ConfigureAwait(false), "invalid_argument", "personal data");
                SqlTestSupport.Check((await store.ListAsync(null, ct).ConfigureAwait(false)).Count == 0, "nothing stored after refusals");
                ToolResult small = await Run("sql_update_context", "{\"content\":" + SqlTestSupport.Json("status values:\n| status | meaning |\n|---|---|\n| open | billable |\n| void | ignore |") + "}", Ctx(store), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(small, "a short enumeration table is fine");
                await store.DeleteAsync(SqlKnowledgeScopes.Database, String.Empty, ct).ConfigureAwait(false);

                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Table, Key = Key("orders"), Content = "Operator notes on orders.", Source = KnowledgeSourceEnum.Operator, UpdatedBy = "anderson@hugo.local" }, ct).ConfigureAwait(false);
                SqlTestSupport.Error(await Run("sql_update_context", "{\"table\":\"orders\",\"mode\":\"replace\",\"content\":\"Agent rewrite.\"}", Ctx(store), ct).ConfigureAwait(false), "permission_denied", "agent replace of operator entry");
                ToolResult appended = await Run("sql_update_context", "{\"table\":\"orders\",\"content\":\"status pending means unpaid.\"}", Ctx(store), ct).ConfigureAwait(false);
                ResourceKnowledgeEntry kept = SqlJson.Deserialize<ResourceKnowledgeEntry>(appended.StructuredJson!)!;
                SqlTestSupport.Check(kept.Source == KnowledgeSourceEnum.Operator && kept.Content.StartsWith("Operator notes on orders.", StringComparison.Ordinal) && kept.Content.EndsWith("status pending means unpaid.", StringComparison.Ordinal), "append keeps operator ownership");
            });

            Add("generate-context", "sql_generate_context drafts database and table context, protects operator entries, masks samples, and never stores rows", async ct =>
            {
                InMemoryResourceKnowledgeStore store = new InMemoryResourceKnowledgeStore();
                await store.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Table, Key = Key("customers"), Content = "Operator-written customer notes.", Source = KnowledgeSourceEnum.Operator }, ct).ConfigureAwait(false);
                ScriptedConnectorModel model = new ScriptedConnectorModel()
                    .On("sql.generate-table-context", "Selected table: " + Key("products"), r => throw new ConnectorException("upstream_error", "provider overloaded"))
                    .On("sql.generate-table-context", "Selected table: " + Key("legacy_accounts"), "Use password=hunter2hunter2 to connect.");

                SqlTestSupport.Error(await Run("sql_generate_context", "{}", Ctx(store, null), ct).ConfigureAwait(false), "not_supported", "no model");
                SqlTestSupport.Error(await Run("sql_generate_context", "{}", Ctx(store, model, false), ct).ConfigureAwait(false), "permission_denied", "knowledge updates forbidden");

                ToolResult r = await Run("sql_generate_context", "{\"concurrency\":2}", Ctx(store, model), ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "generate");
                ContextGenerationResult g = SqlJson.Deserialize<ContextGenerationResult>(r.StructuredJson!)!;
                SqlTestSupport.Check(g.Items.Count == 8 && g.Items[0].Scope == "database" && g.Items[0].Status == "generated", "database first: " + r.Text);
                SqlTestSupport.Check(g.Generated == 5 && g.Skipped == 1 && g.Failed == 2 && g.Model == "scripted-test-model", "counts: " + r.Text);
                SqlTestSupport.Check(g.Items.Single(i => i.Key == Key("customers")).Status == "skipped", "operator entry skipped");
                SqlTestSupport.Check(g.Items.Single(i => i.Key == Key("products")).Reason!.Contains("provider overloaded", StringComparison.Ordinal), "model failure reported");
                SqlTestSupport.Check(g.Items.Single(i => i.Key == Key("legacy_accounts")).Reason!.Contains("not saved", StringComparison.Ordinal), "guard refused generated secret");

                ResourceKnowledgeEntry? database = await store.GetAsync(SqlKnowledgeScopes.Database, String.Empty, ct).ConfigureAwait(false);
                SqlTestSupport.Check(database != null && database.Source == KnowledgeSourceEnum.Generated && database.UpdatedBy == "model scripted-test-model" && database.Content.StartsWith("Shop database", StringComparison.Ordinal) && !database.Content.Contains("```", StringComparison.Ordinal), "database context stored without fences");
                ResourceKnowledgeEntry? orders = await store.GetAsync(SqlKnowledgeScopes.Table, Key("orders"), ct).ConfigureAwait(false);
                SqlTestSupport.Check(orders != null && orders.Content == "Table " + Key("orders") + ": generated context describing its purpose and joins.", "table context stored");
                SqlTestSupport.Check((await store.GetAsync(SqlKnowledgeScopes.Table, Key("customers"), ct).ConfigureAwait(false))!.Content == "Operator-written customer notes.", "operator entry untouched");
                SqlTestSupport.Check((await store.GetAsync(SqlKnowledgeScopes.Table, Key("legacy_accounts"), ct).ConfigureAwait(false)) == null, "refused content not stored");

                ConnectorModelRequest ordersPrompt = model.Requests("sql.generate-table-context").Single(q => q.UserPrompt.Contains("Selected table: " + Key("orders"), StringComparison.Ordinal));
                SqlTestSupport.Check(ordersPrompt.UserPrompt.Contains("Sample rows (3", StringComparison.Ordinal) && ordersPrompt.UserPrompt.Contains("delivered", StringComparison.Ordinal), "sample in the prompt");
                SqlTestSupport.Check(ordersPrompt.UserPrompt.Contains("Shop database", StringComparison.Ordinal), "table prompts see the new database context");
                SqlTestSupport.Check(ordersPrompt.SystemPrompt.Contains("Do not include credentials", StringComparison.Ordinal) && ordersPrompt.Temperature == 0, "system prompt and temperature");
                SqlTestSupport.Check(model.Requests("sql.generate-database-context").Single().UserPrompt.Contains("legacy_invoices.account_id -> ", StringComparison.Ordinal), "database prompt carries inferred relationships");
                foreach (ResourceKnowledgeEntry e in await store.ListAsync(null, ct).ConfigureAwait(false))
                    SqlTestSupport.Check(!e.Content.Contains("@example.com", StringComparison.Ordinal) && !e.Content.Contains("Ada Lovelace", StringComparison.Ordinal), "no rows persisted in " + e.Key);

                ToolResult overwrite = await Run("sql_generate_context", "{\"scope\":\"tables\",\"tables\":[\"customers\"],\"overwrite\":true}", Ctx(store, model), ct).ConfigureAwait(false);
                ContextGenerationResult o = SqlJson.Deserialize<ContextGenerationResult>(overwrite.StructuredJson!)!;
                SqlTestSupport.Check(o.Items.Count == 1 && o.Items[0].Status == "generated" && o.Items[0].UsedSample, "overwrite replaces the operator entry");
                SqlTestSupport.Check((await store.GetAsync(SqlKnowledgeScopes.Table, Key("customers"), ct).ConfigureAwait(false))!.Source == KnowledgeSourceEnum.Generated, "now generated");
                ConnectorModelRequest customersPrompt = model.Requests("sql.generate-table-context").Last();
                SqlTestSupport.Check(customersPrompt.UserPrompt.Contains("(personal data)", StringComparison.Ordinal) && !customersPrompt.UserPrompt.Contains("@example.com", StringComparison.Ordinal), "emails masked in the sample");

                ToolResult noSamples = await Run("sql_generate_context", "{\"scope\":\"tables\",\"tables\":[\"orders\"],\"includeSamples\":false}", Ctx(store, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(!SqlJson.Deserialize<ContextGenerationResult>(noSamples.StructuredJson!)!.Items[0].UsedSample && model.Requests("sql.generate-table-context").Last().UserPrompt.Contains("Sample rows: (not available)", StringComparison.Ordinal), "samples off");
                ToolResult capped = await Run("sql_generate_context", "{\"scope\":\"tables\",\"maxTables\":2}", Ctx(new InMemoryResourceKnowledgeStore(), model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(SqlJson.Deserialize<ContextGenerationResult>(capped.StructuredJson!)!.Remaining == 5 && capped.Text.Contains("call again", StringComparison.Ordinal), "table cap");
                SqlTestSupport.Error(await Run("sql_generate_context", "{\"tables\":[\"nope\"]}", Ctx(store, model), ct).ConfigureAwait(false), "not_found", "unknown table");
            });

            Add("ambiguity", "Ambiguity detection flags terms with several plausible meanings and resolves them by named table, column, or context", async ct =>
            {
                SchemaDetail detail = await CrawlAsync(ct).ConfigureAwait(false);
                SchemaKnowledge none = new SchemaKnowledge();
                List<KeyValuePair<string, string>> table = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("What is the total revenue?", "revenue"),
                    new KeyValuePair<string, string>("What is the total revenue from orders?", String.Empty),
                    new KeyValuePair<string, string>("Show the latest records", "latest"),
                    new KeyValuePair<string, string>("Show the latest orders", String.Empty),
                    new KeyValuePair<string, string>("How many active accounts are there?", "active"),
                    new KeyValuePair<string, string>("How many orders are in each status?", String.Empty),
                    new KeyValuePair<string, string>("Break down records by status", "status"),
                    new KeyValuePair<string, string>("List records by owner", "owner"),
                    new KeyValuePair<string, string>("Who is the owner of each account?", String.Empty),
                    new KeyValuePair<string, string>("How many customers are there?", String.Empty),
                    new KeyValuePair<string, string>("Which client spent the most?", "customer"),
                    new KeyValuePair<string, string>("How many orders were placed in total?", String.Empty),
                    new KeyValuePair<string, string>("What is the total number of products?", String.Empty),
                    new KeyValuePair<string, string>("What were the most recent sales?", "latest,revenue"),
                    new KeyValuePair<string, string>("What is the latest status of invoices?", String.Empty),
                    new KeyValuePair<string, string>("How many invoices does each account have?", String.Empty)
                };
                foreach (KeyValuePair<string, string> c in table)
                {
                    List<string> got = DatabaseIntelligenceBuilder.FindPromptAmbiguities(detail, none, c.Key).Select(a => a.Term).OrderBy(t => t, StringComparer.Ordinal).ToList();
                    string expected = String.Join(",", c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t, StringComparer.Ordinal));
                    SqlTestSupport.Check(String.Join(",", got) == expected, "\"" + c.Key + "\" expected [" + expected + "] got [" + String.Join(",", got) + "]");
                }

                SchemaKnowledge defined = new SchemaKnowledge { Database = new ResourceKnowledgeEntry { Scope = "database", Content = "Revenue means SUM(orders.total) for orders that are not cancelled. Latest refers to orders.ordered_at." } };
                SqlTestSupport.Check(DatabaseIntelligenceBuilder.FindPromptAmbiguities(detail, defined, "What is the total revenue?").Count == 0, "defined revenue");
                SqlTestSupport.Check(DatabaseIntelligenceBuilder.FindPromptAmbiguities(detail, defined, "Show the latest records").Count == 0, "defined latest");
                AmbiguitySignal latest = DatabaseIntelligenceBuilder.FindPromptAmbiguities(detail, none, "Show the latest records").Single();
                SqlTestSupport.Check(latest.Candidates.Count == 4 && latest.Candidates.Contains(Key("orders") + ".ordered_at") && !latest.Candidates.Any(x => x.EndsWith("deleted_at", StringComparison.Ordinal)), "latest candidates: " + String.Join(", ", latest.Candidates));
            });

            Add("ask-statuses", "sql_ask returns Ambiguous, Verified, Partial, and Blocked envelopes with evidence", async ct =>
            {
                ScriptedConnectorModel model = new ScriptedConnectorModel()
                    .On("sql.nl2sql", "Question: How many customers are in GB?", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) AS n FROM customers WHERE country = 'GB';\",\"tables\":[\"customers\"],\"reason\":\"Count GB customers.\"}")
                    .On("sql.nl2sql", "Question: What is the total revenue?", "```json\n{\"execute\":true,\"sql\":\"SELECT SUM(total) AS revenue FROM orders WHERE status <> 'cancelled'\",\"tables\":[\"orders\"],\"reason\":\"Sum non-cancelled order totals.\"}\n```")
                    .On("sql.nl2sql", "Question: List every order id", "{\"execute\":true,\"sql\":\"SELECT order_id FROM orders ORDER BY order_id\"}")
                    .On("sql.nl2sql", "Question: Which tables hold invoices?", "{\"execute\":false,\"answer\":\"legacy_invoices holds invoices; it joins legacy_accounts by account_id (inferred).\"}")
                    .On("sql.nl2sql", "Question: Delete the cancelled orders", "{\"execute\":true,\"sql\":\"DELETE FROM orders WHERE status = 'cancelled'\"}")
                    .On("sql.nl2sql", "Question: Count orders twice", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) FROM orders; SELECT COUNT(*) FROM orders\"}")
                    .On("sql.nl2sql", "Question: How many products are out of stock?", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) AS n FROM products WHERE stock_level = 0\"}")
                    .On("sql.nl2sql-retry", "Question: How many products are out of stock?", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) AS n FROM products WHERE stock = 0\"}")
                    .On("sql.nl2sql", "Question: How many widgets are there?", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) FROM widgets\"}")
                    .On("sql.nl2sql-retry", "Question: How many widgets are there?", "{\"execute\":true,\"sql\":\"SELECT COUNT(*) FROM gadgets\"}")
                    .On("sql.nl2sql", "Question: How many big orders are there?", "{\"execute\":false,\"ambiguous\":true,\"clarification\":\"What order total counts as big?\"}")
                    .On("sql.nl2sql", "Question: Tell me something", "I am not sure what you mean.");

                SqlTestSupport.Error(await Run("sql_ask", "{\"question\":\"How many customers are in GB?\"}", Ctx(null, null), ct).ConfigureAwait(false), "not_supported", "no model");
                SqlTestSupport.Error(await Run("sql_ask", "{\"question\":\"  \"}", Ctx(null, model), ct).ConfigureAwait(false), "invalid_argument", "empty question");

                VerifiedAnswer ambiguous = await Ask("{\"question\":\"What is the total revenue?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(ambiguous.Status == VerificationStatusEnum.Ambiguous && ambiguous.Clarification!.Contains("revenue", StringComparison.Ordinal) && ambiguous.Sql == null, "ambiguous revenue");
                SqlTestSupport.Check(model.Requests().Count == 0, "no model call before clarification");

                VerifiedAnswer verified = await Ask("{\"question\":\"How many customers are in GB?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(verified.Status == VerificationStatusEnum.Verified && verified.RowCount == 1 && !verified.Truncated, "verified count: " + verified.Summary + " " + verified.Error);
                SqlTestSupport.Check(verified.Sql == "SELECT COUNT(*) AS n FROM customers WHERE country = 'GB'" && verified.Classification!.StartsWith("Read", StringComparison.Ordinal), "normalized SQL and classification");
                SqlTestSupport.Check(verified.EvidenceRows.Count == 1 && verified.EvidenceRows[0][0] == "3" && verified.Answer == "The result shows 3.", "answer from the actual rows: " + verified.Answer);
                SqlTestSupport.Check(verified.Evidence.Any(e => e.StartsWith("Hugo ran one read-only", StringComparison.Ordinal)) && verified.Attempts == 1 && verified.Model == "scripted-test-model", "evidence");
                ConnectorModelRequest answerPrompt = model.Requests("sql.answer").Last();
                SqlTestSupport.Check(answerPrompt.UserPrompt.Contains("(complete result)", StringComparison.Ordinal) && answerPrompt.SystemPrompt.Contains("Never invent", StringComparison.Ordinal), "answer prompt grounded in rows");
                ConnectorModelRequest planPrompt = model.Requests("sql.nl2sql").Last();
                SqlTestSupport.Check(planPrompt.UserPrompt.Contains(connector.Dialect.LimitSyntax, StringComparison.Ordinal) && planPrompt.UserPrompt.Contains("Relationships (declared are facts", StringComparison.Ordinal) && planPrompt.UserPrompt.Contains("Likely metrics", StringComparison.Ordinal), "plan prompt carries dialect, relationships, intelligence");

                VerifiedAnswer assumed = await Ask("{\"question\":\"What is the total revenue?\",\"assumptions\":[\"revenue means SUM(orders.total) excluding cancelled orders\"]}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(assumed.Status == VerificationStatusEnum.Verified && assumed.RowCount == 1 && assumed.Assumptions.Count == 1, "assumption resolves ambiguity: " + assumed.Summary);
                SqlTestSupport.Check(model.Requests("sql.nl2sql").Last().UserPrompt.Contains("revenue means SUM(orders.total) excluding cancelled orders", StringComparison.Ordinal), "assumption in the plan prompt");

                InMemoryResourceKnowledgeStore defined = new InMemoryResourceKnowledgeStore();
                await defined.PutAsync(new ResourceKnowledgeEntry { Scope = SqlKnowledgeScopes.Database, Key = String.Empty, Content = "Revenue means SUM(orders.total) for orders that are not cancelled.", Source = KnowledgeSourceEnum.Operator }, ct).ConfigureAwait(false);
                VerifiedAnswer viaContext = await Ask("{\"question\":\"What is the total revenue?\"}", Ctx(defined, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(viaContext.Status == VerificationStatusEnum.Verified, "durable context resolves ambiguity");
                SqlTestSupport.Check(model.Requests("sql.nl2sql").Last().UserPrompt.Contains("Revenue means SUM(orders.total) for orders that are not cancelled.", StringComparison.Ordinal), "database context in the plan prompt");

                VerifiedAnswer truncated = await Ask("{\"question\":\"List every order id\",\"maxRows\":5,\"evidenceRows\":3}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(truncated.Status == VerificationStatusEnum.Partial && truncated.Truncated && truncated.RowCount == 5 && truncated.EvidenceRows.Count == 3, "partial on truncation");
                SqlTestSupport.Check(model.Requests("sql.answer").Last().UserPrompt.Contains("truncated at the row cap", StringComparison.Ordinal), "answer prompt says truncated");

                VerifiedAnswer schemaOnly = await Ask("{\"question\":\"Which tables hold invoices?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(schemaOnly.Status == VerificationStatusEnum.Partial && schemaOnly.Sql == null && schemaOnly.Answer!.Contains("legacy_invoices", StringComparison.Ordinal), "schema-only partial");

                VerifiedAnswer write = await Ask("{\"question\":\"Delete the cancelled orders\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(write.Status == VerificationStatusEnum.Blocked && write.Attempts == 0 && write.Answer == null && write.Classification!.StartsWith("Destructive", StringComparison.Ordinal), "write blocked: " + write.Classification);
                ToolResult count = await Run("sql_query", "{\"sql\":\"SELECT COUNT(*) FROM orders\"}", Ctx(), ct).ConfigureAwait(false);
                SqlTestSupport.Check(SqlToolSuiteBuilder.Parse(count).Rows[0][0] == "12", "nothing was deleted");
                VerifiedAnswer batch = await Ask("{\"question\":\"Count orders twice\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(batch.Status == VerificationStatusEnum.Blocked && batch.Attempts == 0, "batch blocked");

                VerifiedAnswer repaired = await Ask("{\"question\":\"How many products are out of stock?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(repaired.Status == VerificationStatusEnum.Verified && repaired.Attempts == 2 && repaired.EvidenceRows[0][0] == "1" && repaired.Sql!.Contains("stock = 0", StringComparison.Ordinal), "repaired after one error: " + repaired.Summary + " " + repaired.Error);
                SqlTestSupport.Check(model.Requests("sql.nl2sql-retry").Last().UserPrompt.Contains("stock_level", StringComparison.Ordinal) && model.Requests("sql.nl2sql-retry").Last().UserPrompt.Contains("Database error:", StringComparison.Ordinal), "retry prompt carries the error");
                SqlTestSupport.Check(repaired.Evidence.Any(e => e.StartsWith("Attempt 1 failed", StringComparison.Ordinal)), "failed attempt recorded");

                VerifiedAnswer failed = await Ask("{\"question\":\"How many widgets are there?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(failed.Status == VerificationStatusEnum.Blocked && failed.Attempts == 2 && failed.Error != null && failed.Answer == null, "blocked after retry");

                VerifiedAnswer plannerAmbiguous = await Ask("{\"question\":\"How many big orders are there?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(plannerAmbiguous.Status == VerificationStatusEnum.Ambiguous && plannerAmbiguous.Clarification == "What order total counts as big?", "planner clarification");

                int before = model.Requests("sql.nl2sql").Count;
                VerifiedAnswer garbage = await Ask("{\"question\":\"Tell me something\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(garbage.Status == VerificationStatusEnum.Blocked && model.Requests("sql.nl2sql").Count == before + 2, "unusable plan blocked after one reformat request");

                ToolResult text = await Run("sql_ask", "{\"question\":\"How many customers are in GB?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                SqlTestSupport.Check(text.Text.StartsWith("Status: Verified", StringComparison.Ordinal) && text.Text.Contains("SQL: SELECT COUNT(*)", StringComparison.Ordinal) && text.Text.Contains("Evidence rows", StringComparison.Ordinal), "text envelope: " + text.Text);
            });

            Add("ask-table-selection", "sql_ask selects relevant tables first on large schemas and adds join neighbors", async ct =>
            {
                SqlConnectorBase small = target.Factory();
                SqlAskTool ask = (SqlAskTool)SqlTestSupport.Tool(small, "sql_ask");
                ask.FullSchemaTableLimit = 3;
                ScriptedConnectorModel model = new ScriptedConnectorModel()
                    .On("sql.select-tables", "How many invoices does each account have?", "{\"tables\":[\"legacy_invoices\"]}")
                    .On("sql.nl2sql", "Question: How many invoices does each account have?", "{\"execute\":true,\"sql\":\"SELECT account_id, COUNT(*) AS invoices FROM legacy_invoices GROUP BY account_id ORDER BY account_id\",\"tables\":[\"legacy_invoices\"]}");
                ToolResult r = await SqlTestSupport.RunAsync(small, "sql_ask", "{\"question\":\"How many invoices does each account have?\"}", Ctx(null, model), ct).ConfigureAwait(false);
                VerifiedAnswer a = SqlJson.Deserialize<VerifiedAnswer>(r.StructuredJson!)!;
                SqlTestSupport.Check(a.Status == VerificationStatusEnum.Verified && a.RowCount == 4, "verified grouped answer: " + r.Text);
                string selectPrompt = model.Requests("sql.select-tables").Single().UserPrompt;
                SqlTestSupport.Check(selectPrompt.Contains(Key("legacy_accounts") + ":", StringComparison.Ordinal) && selectPrompt.Contains("at most 12 tables", StringComparison.Ordinal), "selection prompt lists the index");
                string plan = model.Requests("sql.nl2sql").Single().UserPrompt;
                SqlTestSupport.Check(plan.Contains("Schema (2 of 7 tables)", StringComparison.Ordinal) && plan.Contains("- " + Key("legacy_accounts") + " (", StringComparison.Ordinal) && plan.Contains("Other tables not shown", StringComparison.Ordinal), "only selected tables and the inferred neighbor: " + plan);
                SqlTestSupport.Check(!plan.Contains("- " + Key("order_items") + " (", StringComparison.Ordinal), "unrelated tables left out");
            });

            return cases;

            async Task<VerifiedAnswer> Ask(string json, ConnectorInvocationContext context, CancellationToken ct)
            {
                ToolResult r = await Run("sql_ask", json, context, ct).ConfigureAwait(false);
                SqlTestSupport.Ok(r, "sql_ask " + json);
                return SqlJson.Deserialize<VerifiedAnswer>(r.StructuredJson!)!;
            }
        }

        #endregion

        #region Private-Methods

        private static SchemaPage<T> Page<T>(ToolResult result)
        {
            if (result.IsError || String.IsNullOrEmpty(result.StructuredJson)) throw new InvalidOperationException("Expected a page but got " + result.ErrorCode + ": " + result.Text);
            return SqlJson.Deserialize<SchemaPage<T>>(result.StructuredJson)!;
        }

        private static DomainEntity Entity(DatabaseIntelligence intelligence, string table)
        {
            return intelligence.Domain.Entities.Single(e => e.TableName == table);
        }

        #endregion
    }
}
