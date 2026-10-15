// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/DatabaseDrivers/SqlServerCrawler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed, catalog SQL only (plumbing in SchemaCrawlerBase), views and row estimates added,
// primary key join includes the table name, foreign keys report the referenced schema, primary index flag, included
// columns excluded from index keys. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    /// <summary>
    /// SQL Server catalog crawler. Stateless and thread-safe.
    /// </summary>
    public class SqlServerSchemaCrawler : SchemaCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Engine
        {
            get
            {
                return "sqlserver";
            }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string ListTablesSql
        {
            get
            {
                return "SELECT o.name, CASE o.type WHEN 'V' THEN 'VIEW' ELSE 'BASE TABLE' END, "
                    + "(SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0, 1)) "
                    + "FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id "
                    + "WHERE s.name = @schema AND o.type IN ('U', 'V') AND o.is_ms_shipped = 0 "
                    + "ORDER BY o.name";
            }
        }

        /// <inheritdoc />
        protected override string ColumnsSql
        {
            get
            {
                return "SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, CHARACTER_MAXIMUM_LENGTH, ORDINAL_POSITION "
                    + "FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table "
                    + "ORDER BY ORDINAL_POSITION";
            }
        }

        /// <inheritdoc />
        protected override string PrimaryKeysSql
        {
            get
            {
                return "SELECT KCU.COLUMN_NAME "
                    + "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS TC "
                    + "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE KCU ON TC.CONSTRAINT_NAME = KCU.CONSTRAINT_NAME "
                    + "AND TC.TABLE_SCHEMA = KCU.TABLE_SCHEMA AND TC.TABLE_NAME = KCU.TABLE_NAME "
                    + "WHERE TC.TABLE_SCHEMA = @schema AND TC.TABLE_NAME = @table AND TC.CONSTRAINT_TYPE = 'PRIMARY KEY' "
                    + "ORDER BY KCU.ORDINAL_POSITION";
            }
        }

        /// <inheritdoc />
        protected override string ForeignKeysSql
        {
            get
            {
                return "SELECT fk.name, cp.name, rs.name, rt.name, cr.name "
                    + "FROM sys.foreign_keys fk "
                    + "JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id "
                    + "JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id "
                    + "JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id "
                    + "JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id "
                    + "JOIN sys.schemas rs ON rt.schema_id = rs.schema_id "
                    + "JOIN sys.tables pt ON fkc.parent_object_id = pt.object_id "
                    + "JOIN sys.schemas s ON pt.schema_id = s.schema_id "
                    + "WHERE s.name = @schema AND pt.name = @table "
                    + "ORDER BY fk.name, fkc.constraint_column_id";
            }
        }

        /// <inheritdoc />
        protected override string IndexesSql
        {
            get
            {
                return "SELECT i.name, c.name, i.is_unique, i.is_primary_key "
                    + "FROM sys.indexes i "
                    + "JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id "
                    + "JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id "
                    + "JOIN sys.objects t ON i.object_id = t.object_id "
                    + "JOIN sys.schemas s ON t.schema_id = s.schema_id "
                    + "WHERE s.name = @schema AND t.name = @table AND i.name IS NOT NULL AND ic.is_included_column = 0 "
                    + "ORDER BY i.name, ic.key_ordinal";
            }
        }

        #endregion
    }
}
