// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/DatabaseDrivers/PostgresCrawler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed, catalog SQL only (plumbing in SchemaCrawlerBase), views and row estimates added,
// primary key join includes the table name, indexes read from pg_index instead of parsing pg_indexes.indexdef,
// foreign keys report the referenced schema. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    /// <summary>
    /// PostgreSQL catalog crawler. Stateless and thread-safe.
    /// </summary>
    public class PostgresqlSchemaCrawler : SchemaCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Engine
        {
            get
            {
                return "postgresql";
            }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string ListTablesSql
        {
            get
            {
                return "SELECT c.relname, "
                    + "CASE c.relkind WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' WHEN 'f' THEN 'FOREIGN TABLE' ELSE 'BASE TABLE' END, "
                    + "CASE WHEN c.relkind IN ('v', 'f') OR c.reltuples < 0 THEN NULL ELSE c.reltuples::bigint END "
                    + "FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace "
                    + "WHERE n.nspname = @schema AND c.relkind IN ('r', 'p', 'v', 'm', 'f') "
                    + "ORDER BY c.relname";
            }
        }

        /// <inheritdoc />
        protected override string ColumnsSql
        {
            get
            {
                return "SELECT column_name, "
                    + "CASE WHEN data_type IN ('USER-DEFINED', 'ARRAY') THEN udt_name ELSE data_type END, "
                    + "is_nullable, column_default, character_maximum_length, ordinal_position "
                    + "FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table "
                    + "ORDER BY ordinal_position";
            }
        }

        /// <inheritdoc />
        protected override string PrimaryKeysSql
        {
            get
            {
                return "SELECT kcu.column_name "
                    + "FROM information_schema.table_constraints tc "
                    + "JOIN information_schema.key_column_usage kcu ON tc.constraint_name = kcu.constraint_name "
                    + "AND tc.table_schema = kcu.table_schema AND tc.table_name = kcu.table_name "
                    + "WHERE tc.table_schema = @schema AND tc.table_name = @table AND tc.constraint_type = 'PRIMARY KEY' "
                    + "ORDER BY kcu.ordinal_position";
            }
        }

        /// <inheritdoc />
        protected override string ForeignKeysSql
        {
            get
            {
                return "SELECT rc.constraint_name, kcu.column_name, ccu.table_schema, ccu.table_name, ccu.column_name "
                    + "FROM information_schema.referential_constraints rc "
                    + "JOIN information_schema.key_column_usage kcu ON rc.constraint_name = kcu.constraint_name AND rc.constraint_schema = kcu.constraint_schema "
                    + "JOIN information_schema.constraint_column_usage ccu ON rc.unique_constraint_name = ccu.constraint_name AND rc.unique_constraint_schema = ccu.constraint_schema "
                    + "WHERE kcu.table_schema = @schema AND kcu.table_name = @table "
                    + "ORDER BY rc.constraint_name, kcu.ordinal_position";
            }
        }

        /// <inheritdoc />
        protected override string IndexesSql
        {
            get
            {
                return "SELECT i.relname, a.attname, ix.indisunique, ix.indisprimary "
                    + "FROM pg_catalog.pg_index ix "
                    + "JOIN pg_catalog.pg_class t ON t.oid = ix.indrelid "
                    + "JOIN pg_catalog.pg_class i ON i.oid = ix.indexrelid "
                    + "JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace "
                    + "CROSS JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) "
                    + "LEFT JOIN pg_catalog.pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum "
                    + "WHERE n.nspname = @schema AND t.relname = @table "
                    + "ORDER BY i.relname, k.ord";
            }
        }

        #endregion
    }
}
