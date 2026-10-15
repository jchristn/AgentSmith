// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/DatabaseDrivers/MysqlCrawler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed, catalog SQL only (plumbing in SchemaCrawlerBase), views and row estimates added,
// COLUMN_TYPE instead of DATA_TYPE, primary key join includes the table name (every MySQL primary key constraint is
// named PRIMARY), foreign keys read from KEY_COLUMN_USAGE with the referenced schema, primary index flag.
// See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    /// <summary>
    /// MySQL catalog crawler. The schema is the MySQL database name. Stateless and thread-safe.
    /// </summary>
    public class MysqlSchemaCrawler : SchemaCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Engine
        {
            get
            {
                return "mysql";
            }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string ListTablesSql
        {
            get
            {
                return "SELECT table_name, table_type, table_rows FROM information_schema.tables "
                    + "WHERE table_schema = @schema ORDER BY table_name";
            }
        }

        /// <inheritdoc />
        protected override string ColumnsSql
        {
            get
            {
                return "SELECT column_name, column_type, is_nullable, column_default, character_maximum_length, ordinal_position "
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
                return "SELECT constraint_name, column_name, referenced_table_schema, referenced_table_name, referenced_column_name "
                    + "FROM information_schema.key_column_usage "
                    + "WHERE table_schema = @schema AND table_name = @table AND referenced_table_name IS NOT NULL "
                    + "ORDER BY constraint_name, ordinal_position";
            }
        }

        /// <inheritdoc />
        protected override string IndexesSql
        {
            get
            {
                return "SELECT index_name, column_name, CASE non_unique WHEN 0 THEN 1 ELSE 0 END, "
                    + "CASE index_name WHEN 'PRIMARY' THEN 1 ELSE 0 END "
                    + "FROM information_schema.statistics "
                    + "WHERE table_schema = @schema AND table_name = @table ORDER BY index_name, seq_in_index";
            }
        }

        #endregion
    }
}
