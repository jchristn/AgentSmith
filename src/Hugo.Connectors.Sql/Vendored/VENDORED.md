# Vendored code

## Tablix

- **Source:** https://github.com/jchristn/Tablix
- **Commit:** `450bc764be478740db5bfc9ae9cb3fd4bb9c14c6`
- **License:** MIT, Copyright (c) 2026 Joel Christner and Adam Wingate
- **Vendored on:** 2026-10-14 (plan task P0.15)
- **Location:** `Vendored/Tablix/`, namespace `Hugo.Connectors.Sql.Vendored.Tablix`

Every vendored file starts with a header naming its Tablix source file, the commit, and the license.

### What was taken

| Hugo file | Tablix source |
|---|---|
| `ISchemaCrawler.cs` | `src/Tablix.Core/DatabaseDrivers/IDatabaseCrawler.cs` |
| `SchemaCrawlerBase.cs` | shared crawl loop and reader code of the four crawlers below |
| `SqliteSchemaCrawler.cs` | `src/Tablix.Core/DatabaseDrivers/SqliteCrawler.cs` |
| `PostgresqlSchemaCrawler.cs` | `src/Tablix.Core/DatabaseDrivers/PostgresCrawler.cs` |
| `MysqlSchemaCrawler.cs` | `src/Tablix.Core/DatabaseDrivers/MysqlCrawler.cs` |
| `SqlServerSchemaCrawler.cs` | `src/Tablix.Core/DatabaseDrivers/SqlServerCrawler.cs` |
| `SchemaDetail.cs` | `src/Tablix.Core/Models/DatabaseDetail.cs` |
| `TableDetail.cs` | `src/Tablix.Core/Models/TableDetail.cs` |
| `ColumnDetail.cs` | `src/Tablix.Core/Models/ColumnDetail.cs` |
| `ForeignKeyDetail.cs` | `src/Tablix.Core/Models/ForeignKeyDetail.cs` |
| `IndexDetail.cs` | `src/Tablix.Core/Models/IndexDetail.cs` |
| `QueryValidator.cs` | `src/Tablix.Core/Helpers/QueryValidator.cs` |

`Settings/DatabaseEntry.cs` and `Models/QueryResult.cs` were studied but not copied: Hugo reads connection settings from
`ConnectorResourceContext` (`SqlResourceOptions`) and returns rows as `SqlQueryResult`.

### What changed

General:

- Namespaces moved to `Hugo.Connectors.Sql.Vendored.Tablix`; code reformatted to the Hugo style in `CLAUDE.md`
  (usings inside the namespace, braces, XML documentation, nullable reference types).
- Tablix-only members removed: `DatabaseId`, `Context`, `IsCrawled`, `TableId`, crawl progress callbacks, and the
  `SerializableDataTables` dependency.

Crawlers:

- The crawlers no longer build connection strings or open connections. They receive an open `DbConnection` and an
  optional `DbTransaction`, so Hugo controls credentials, TLS, timeouts, and read-only transactions. Query execution and
  connection tests moved to the connector (`SqlExecutor`, `SqlConnectorBase`).
- The per-engine crawl loop, primary key merge, and reader code were lifted into `SchemaCrawlerBase` over
  `System.Data.Common`; each engine now supplies only catalog SQL.
- Added `ListTablesAsync` and `DescribeTableAsync` alongside `CrawlAsync`.
- Views (and PostgreSQL materialized and foreign tables) are listed with a `TableType`; Tablix listed base tables only.
- Row estimates added: `pg_class.reltuples`, `information_schema.tables.table_rows`, `sys.partitions.rows`, and an
  exact `COUNT(*)` for SQLite.
- Columns are ordered by ordinal position (Tablix had no `ORDER BY`).
- Primary key queries join on the table name as well as the constraint name. In MySQL every primary key constraint is
  named `PRIMARY`, so the Tablix join could attribute another table's key columns.
- SQLite: `PRAGMA ... ('" + tableName + "')` string concatenation was replaced with parameterized pragma table-valued
  functions (`pragma_table_info(@table, @schema)` and friends), attached schemas are supported, and foreign keys whose
  referenced column is implicit (NULL) no longer throw.
- PostgreSQL: indexes come from `pg_index` with key order and a primary flag instead of parsing
  `pg_indexes.indexdef`; `USER-DEFINED` and `ARRAY` columns report their `udt_name`.
- MySQL: `COLUMN_TYPE` (for example `varchar(255)`, `int unsigned`) replaces `DATA_TYPE`; foreign keys come straight
  from `KEY_COLUMN_USAGE`; functional index parts report `(expression)`.
- SQL Server: included columns are excluded from index keys.
- Foreign keys report the referenced schema; indexes report whether they back the primary key.
- `ColumnDetail.MaxLength` is 64-bit (MySQL `longtext` lengths overflowed the Tablix `int` cast).

QueryValidator:

- Kept as `QueryValidator` with nullable annotations and Hugo formatting. Its keyword-first approach, CTE walk, and
  quote and comment skipping are the basis of `StatementClassifier` and `SqlTokenizer`, which extend it with a
  dialect-aware tokenizer (MySQL backslash escapes, `#` and executable `/*! */` comments, the `-- ` rule; PostgreSQL
  dollar quoting, `E''` strings, nested comments; bracket and backtick identifiers), multi-statement handling, CTE
  bodies, `EXPLAIN ANALYZE`, implicit T-SQL statement boundaries, dynamic SQL in literals, side-effecting functions, and
  Read/Write/Destructive effects. `QueryValidator.GetStatementType` remains available for bounded metric labels.

## Tablix database intelligence (D-038)

- **Source:** https://github.com/jchristn/Tablix
- **Commit:** `450bc764be478740db5bfc9ae9cb3fd4bb9c14c6` (same commit as the crawlers above)
- **License:** MIT, Copyright (c) 2026 Joel Christner and Adam Wingate
- **Ported on:** 2026-10-14 (plan tasks P4.31 to P4.35)
- **Location:** `Intelligence/` (namespace `Hugo.Connectors.Sql.Intelligence`) and the tools in `Tools/`

These files are ports rather than verbatim copies: Tablix keeps intelligence and chat inside its own server and
database, while Hugo runs them inside connector tools against the resource knowledge store. Every ported file starts
with a header naming its Tablix source, the commit, the license, and what changed.

### What was taken

| Hugo file | Tablix source |
|---|---|
| `Intelligence/DatabaseIntelligenceBuilder.cs` | `src/Tablix.Core/Helpers/DatabaseIntelligenceBuilder.cs` |
| `Intelligence/NameHeuristics.cs` | the private name helpers of `DatabaseIntelligenceBuilder.cs` |
| `Intelligence/AgentPackBuilder.cs` | `DatabaseIntelligenceBuilder.BuildAgentPack` |
| `Intelligence/SchemaProjection.cs` | `src/Tablix.Core/Helpers/SchemaProjection.cs` |
| `Intelligence/TableIdentity.cs` | `src/Tablix.Core/Helpers/TableIdentity.cs` and `ChatHandler.FindTablesByName` |
| `Intelligence/RelationshipDetail.cs` | `src/Tablix.Core/Models/RelationshipDetail.cs` |
| `Intelligence/AmbiguitySignal.cs` | `src/Tablix.Core/Models/AmbiguitySignal.cs` |
| `Intelligence/ContextQualityScore.cs`, `ContextQualitySignal.cs` | `src/Tablix.Core/Models/ContextQualityScore.cs`, `ContextQualitySignal.cs` |
| `Intelligence/DomainEntity.cs`, `DomainIntelligence.cs` | `src/Tablix.Core/Models/DomainEntity.cs`, `DomainIntelligence.cs` |
| `Intelligence/DatabaseIntelligence.cs` | `src/Tablix.Core/Models/DatabaseIntelligenceResponse.cs` |
| `Intelligence/AgentPack.cs` | `src/Tablix.Core/Models/AgentPackResponse.cs` |
| `Intelligence/TableSummary.cs` | `src/Tablix.Core/Models/TableSummary.cs` |
| `Intelligence/SchemaPage.cs` | `src/Tablix.Core/Models/EnumerationResult.cs`, `DatabaseTableListResult.cs`, `DatabaseRelationshipListResult.cs` |
| `Intelligence/VerifiedAnswer.cs` | `src/Tablix.Core/Models/VerifiedAnswer.cs` |
| `Intelligence/QueryPlan.cs` | `src/Tablix.Server/Handlers/FallbackQueryPlan.cs` |
| `Intelligence/SqlPrompts.cs` | prompt builders in `src/Tablix.Server/Handlers/ChatHandler.cs` (`MandatoryExecutionSystemPrompt`, `BuildPrompt`, `DefaultContextBuildInstructions`, `DefaultTableContextBuildInstructions`, `BuildContextPrompt`, `BuildTableContextPrompt`, `BuildFallbackPlannerPrompt`, `BuildToolFollowupPrompt`) |
| `Intelligence/ModelText.cs` | `ChatHandler.ExtractJsonPayload`, `ParseFallbackPlan`, `NormalizeGeneratedContext` |
| `Tools/SqlAskTool.cs` | flow of `ChatHandler.PrepareChatAsync`, `ExecuteFallbackPlanningAsync`, `ExecutePlannedQueryAsync`, `BuildVerifiedAnswer`, and `src/Tablix.Server/ChatQueryExecutionService.cs` |
| `Tools/SqlGenerateContextTool.cs` | `ChatHandler.BuildContextCoreAsync`, `GenerateTableContextsAsync`, `GenerateOneTableContextAsync` |
| `Tools/SqlUpdateContextTool.cs` | `ChatHandler.ExecuteDatabaseContextUpdateToolCallAsync`, `ExecuteTableContextUpdateToolCallAsync`, and the `tablix_update_context` MCP tool |
| Tool descriptions and `Instructions/*.md` | tool descriptions and agent workflow guidance in `src/Tablix.Server/Mcp/McpToolRegistrar.cs`, `MCP_API.md`, and the README agent best practices |

Studied but not ported: `src/Tablix.Server/Handlers/DatabaseHandler.cs` and `CrawlCache.cs` (Hugo's crawl cache is
`SchemaCache`; persisting it belongs to the server), `TablixChatToolDefinitions.cs` and the native tool-call chat loop
(Hugo's agent runtime owns tool calling; `sql_ask` is one tool call), provider clients and Gemini response parsing
(Hugo reaches models through `IConnectorModel`), streaming events, telemetry, and the setup wizard.

### What changed

Context and identity:

- Tablix stores context in `Context` fields on its crawl models and its own `context_records` table. Hugo reads and
  writes `IResourceKnowledgeStore` with scopes `database` (key empty) and `table` (key `schema.table`), wrapped by
  `SchemaKnowledge`. Entries carry a source (`Operator`, `Generated`, `Agent`), a version, and who changed them.
- Tablix table IDs (`tbl_...`) are replaced by the `schema.table` key.

Relationship inference (Tablix `BuildInferredRelationships`):

- Kept: declared keys at confidence 1.0; candidates from `<entity>_id` columns whose entity matches a table name
  (singular or plural); base confidence 0.74, 0.86 when the column is exactly `<singular table>id`, plus 0.04 when the
  target key is `id`; capped at 0.95.
- Added: a column with a declared key is never inferred again; views can be sources but never targets; a table name
  with a leading prefix still matches (`legacy_accounts` for `account_id`, minus 0.06); a composite primary key is not
  guessed; plus 0.04 when the column name equals the target key, minus 0.15 when the target is not unique, minus 0.30
  when the types cannot hold the same values, plus 0.02 when the source column is indexed; every edge lists its
  evidence. Key detection needs a word boundary before `id` (`customer_id`, `customerId`, not `paid` or `uuid`).
  SQLite keys with an implicit referenced column resolve to the referenced primary key.

Domain intelligence:

- Role, filter, status, activity, and soft-delete keywords match whole words, so `catalog` is not an event table and
  `statement` is not a status column. Type, kind, and category columns count as filters.
- Freshness columns include any `*_at` or `*_on` timestamp, accept text-typed timestamps with temporal names (SQLite),
  and exclude soft-delete timestamps. Metrics never include key columns and carry natural aggregations
  (`MetricCandidate`): per-unit prices average, balances take the latest value.
- The context quality score keeps Tablix's weights; the crawl always counts as successful, and an info signal reports
  model-generated table context awaiting operator review.

Ambiguity detection (Tablix `BuildAmbiguities` and `FindPromptAmbiguities`):

- Kept: the terms latest, active, status, revenue, owner, and customer, each needing two or more candidates, and the
  business_context signal when no database context exists.
- Added: a term resolves when the question names exactly one candidate table (plural, singular, or without a prefix)
  or one candidate column. Tablix's explicit-candidate test compared only the column name, which for `status` is the
  term itself, so status could never be ambiguous. Saved context that defines a term ("revenue means ...") resolves
  it. Views are not party tables. Weak triggers (total, amount, last) do not fire on counting questions.

Projection and pack:

- An opaque cursor replaces skip and NextSkip; search also matches column names; relationships filter by table at
  either end and by minimum confidence; inferred candidates are included by default (the Tablix MCP tool ignored
  `includeInferred`).
- The agent pack includes the database context and shortened table contexts instead of pointing at them, uses Hugo
  tool names, and reports what its caps omitted.

Questions to verified answers (`sql_ask`):

- One tool call instead of a chat loop with native tool calls. Ambiguity is checked before planning and stops with
  status Ambiguous unless the caller passes assumptions. Large schemas get a table selection step first.
- Generated SQL must classify as a single Read through `StatementClassifier` (Tablix checked an allow list of
  statement types); it runs in a read-only session with the resource's row cap and timeout. Every failure is retried
  once with the database error (Tablix retried only schema-related errors, after a schema refresh; Hugo also recrawls
  in that case).
- The verification envelope adds the answer text (written by the model only from the returned rows), the
  classification, bounded evidence rows, assumptions, clarification, and attempts. Truncated results and schema-only
  answers are Partial.

Context generation (`sql_generate_context`):

- Prompts add inferred relationships, intelligence (metrics, filters), row estimates, and a few masked sample rows
  (`SampleMasker`: secret-like columns redacted, personal-data columns replaced, long values cut). Samples never leave
  the prompt.
- Operator- and agent-authored entries are kept unless `overwrite` is true; tables without context go first; a cap and
  a concurrency limit bound each call; generated text passes `KnowledgeContentGuard` before it is stored.

### Updating

Diff the Tablix sources above between `450bc764be478740db5bfc9ae9cb3fd4bb9c14c6` and the new commit, port fixes into
the matching Hugo files by hand, update the commit in each file header and here, and rerun
`dotnet run --project src/Test.Automated -f net10.0 -- --suites connectors.sql`.
