// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/DatabaseDrivers/SqliteCrawler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed, catalog SQL only (plumbing in SchemaCrawlerBase), PRAGMA statements with concatenated
// table names replaced by parameterized pragma table-valued functions, attached schemas supported, views added,
// exact row counts, NULL referenced columns tolerated. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// SQLite catalog crawler. The schema is <c>main</c> or the name of an attached database. Stateless and thread-safe.
    /// </summary>
    public class SqliteSchemaCrawler : SchemaCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Engine
        {
            get
            {
                return "sqlite";
            }
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override string ListTablesSql
        {
            get
            {
                return "SELECT name, type, NULL FROM pragma_table_list "
                    + "WHERE schema = @schema AND type IN ('table', 'view', 'virtual') AND substr(name, 1, 7) <> 'sqlite_' "
                    + "ORDER BY name";
            }
        }

        /// <inheritdoc />
        protected override string ColumnsSql
        {
            get
            {
                return "SELECT name, COALESCE(type, ''), CASE \"notnull\" WHEN 0 THEN 'YES' ELSE 'NO' END, dflt_value, NULL, cid + 1 "
                    + "FROM pragma_table_info(@table, @schema) ORDER BY cid";
            }
        }

        /// <inheritdoc />
        protected override string PrimaryKeysSql
        {
            get
            {
                return "SELECT name FROM pragma_table_info(@table, @schema) WHERE pk > 0 ORDER BY pk";
            }
        }

        /// <inheritdoc />
        protected override string ForeignKeysSql
        {
            get
            {
                return "SELECT 'fk_' || @table || '_' || id, \"from\", @schema, \"table\", \"to\" "
                    + "FROM pragma_foreign_key_list(@table, @schema) ORDER BY id, seq";
            }
        }

        /// <inheritdoc />
        protected override string IndexesSql
        {
            get
            {
                return "SELECT il.name, ii.name, il.\"unique\", CASE il.origin WHEN 'pk' THEN 1 ELSE 0 END "
                    + "FROM pragma_index_list(@table, @schema) il JOIN pragma_index_info(il.name, @schema) ii "
                    + "ORDER BY il.name, ii.seqno";
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// SQLite keeps no row statistics by default, so the estimate is an exact <c>COUNT(*)</c> for tables. The table
        /// name was read from the catalog and is quoted as an identifier; it is never a caller-supplied value.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="table">Table.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row count, or null for views.</returns>
        protected override async Task<long?> ReadRowEstimateAsync(DbConnection connection, DbTransaction? transaction, TableDetail table, CancellationToken token)
        {
            if (!String.Equals(table.TableType, "BASE TABLE", StringComparison.Ordinal)) return null;
            using (DbCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT COUNT(*) FROM " + QuoteIdentifier(table.SchemaName) + "." + QuoteIdentifier(table.TableName);
                object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                if (value == null || value is DBNull) return null;
                return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        #endregion

        #region Private-Methods

        private static string QuoteIdentifier(string name)
        {
            return "\"" + name.Replace("\"", "\"\"") + "\"";
        }

        #endregion
    }
}
