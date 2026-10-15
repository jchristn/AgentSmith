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

### Updating

Diff the Tablix sources above between `450bc764be478740db5bfc9ae9cb3fd4bb9c14c6` and the new commit, port fixes into
the matching Hugo files by hand, update the commit in each file header and here, and rerun
`dotnet run --project src/Test.Automated -f net10.0 -- --suites connectors.sql`.
