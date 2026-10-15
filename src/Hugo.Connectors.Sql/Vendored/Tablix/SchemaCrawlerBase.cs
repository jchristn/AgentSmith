// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// shared logic of src/Tablix.Core/DatabaseDrivers/{Sqlite,Postgres,Mysql,SqlServer}Crawler.cs.
// Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: the per-engine crawl loop, primary key merge, and reader plumbing were lifted into this base class
// over System.Data.Common so each engine supplies only its catalog SQL; progress callbacks removed; every value is a
// parameter. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Catalog-driven crawler. Derived classes supply SQL whose result shapes are documented on each member; every query
    /// takes the parameters <c>@schema</c> and, where noted, <c>@table</c>. Stateless and thread-safe.
    /// </summary>
    public abstract class SchemaCrawlerBase : ISchemaCrawler
    {
        #region Public-Members

        /// <summary>
        /// Engine name recorded on crawl results (for example <c>postgresql</c>).
        /// </summary>
        public abstract string Engine { get; }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Tables and views: (name, type, row estimate or NULL). Parameters: <c>@schema</c>.
        /// </summary>
        protected abstract string ListTablesSql { get; }

        /// <summary>
        /// Columns: (name, data type, nullable as YES or NO, default or NULL, max length or NULL, ordinal).
        /// Parameters: <c>@schema</c>, <c>@table</c>.
        /// </summary>
        protected abstract string ColumnsSql { get; }

        /// <summary>
        /// Primary key column names in key order. Parameters: <c>@schema</c>, <c>@table</c>.
        /// </summary>
        protected abstract string PrimaryKeysSql { get; }

        /// <summary>
        /// Foreign keys: (constraint, column, referenced schema or NULL, referenced table, referenced column or NULL).
        /// Parameters: <c>@schema</c>, <c>@table</c>.
        /// </summary>
        protected abstract string ForeignKeysSql { get; }

        /// <summary>
        /// Index columns: (index name, column name or NULL for expressions, unique flag, primary flag), ordered by index
        /// name then key position. Parameters: <c>@schema</c>, <c>@table</c>.
        /// </summary>
        protected abstract string IndexesSql { get; }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public virtual async Task<List<TableDetail>> ListTablesAsync(DbConnection connection, DbTransaction? transaction, string schema, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (String.IsNullOrEmpty(schema)) throw new ArgumentNullException(nameof(schema));

            List<TableDetail> tables = new List<TableDetail>();
            using (DbCommand command = CreateCommand(connection, transaction, ListTablesSql, schema, null))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        TableDetail table = new TableDetail
                        {
                            SchemaName = schema,
                            TableName = ReadString(reader, 0) ?? String.Empty,
                            TableType = NormalizeTableType(ReadString(reader, 1)),
                            RowEstimate = ReadLong(reader, 2)
                        };
                        tables.Add(table);
                    }
                }
            }
            return tables.OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <inheritdoc />
        public virtual async Task<TableDetail?> DescribeTableAsync(DbConnection connection, DbTransaction? transaction, string schema, string tableName, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (String.IsNullOrEmpty(schema)) throw new ArgumentNullException(nameof(schema));
            if (String.IsNullOrEmpty(tableName)) throw new ArgumentNullException(nameof(tableName));

            List<TableDetail> tables = await ListTablesAsync(connection, transaction, schema, token).ConfigureAwait(false);
            TableDetail? table = tables.FirstOrDefault(t => String.Equals(t.TableName, tableName, StringComparison.Ordinal))
                ?? tables.FirstOrDefault(t => String.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
            if (table == null) return null;

            await PopulateAsync(connection, transaction, table, token).ConfigureAwait(false);
            return table;
        }

        /// <inheritdoc />
        public virtual async Task<SchemaDetail> CrawlAsync(DbConnection connection, DbTransaction? transaction, string schema, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (String.IsNullOrEmpty(schema)) throw new ArgumentNullException(nameof(schema));

            SchemaDetail detail = new SchemaDetail
            {
                Engine = Engine,
                DatabaseName = connection.Database,
                Schema = schema
            };

            List<TableDetail> tables = await ListTablesAsync(connection, transaction, schema, token).ConfigureAwait(false);
            foreach (TableDetail table in tables)
            {
                token.ThrowIfCancellationRequested();
                await PopulateAsync(connection, transaction, table, token).ConfigureAwait(false);
                detail.Tables.Add(table);
            }

            detail.CrawledUtc = DateTime.UtcNow;
            return detail;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Read the row estimate for a table after its columns are known. The default keeps the estimate from
        /// <see cref="ListTablesSql"/>.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="table">Table.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Estimate, or null when unknown.</returns>
        protected virtual Task<long?> ReadRowEstimateAsync(DbConnection connection, DbTransaction? transaction, TableDetail table, CancellationToken token)
        {
            return Task.FromResult(table.RowEstimate);
        }

        /// <summary>
        /// Build a command with <c>@schema</c> and optional <c>@table</c> parameters.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="sql">SQL text.</param>
        /// <param name="schema">Schema value.</param>
        /// <param name="table">Table value, or null to omit the parameter.</param>
        /// <returns>Command; the caller disposes it.</returns>
        protected static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string sql, string schema, string? table)
        {
            DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            DbParameter schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = "@schema";
            schemaParameter.Value = schema;
            command.Parameters.Add(schemaParameter);
            if (table != null)
            {
                DbParameter tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "@table";
                tableParameter.Value = table;
                command.Parameters.Add(tableParameter);
            }
            return command;
        }

        /// <summary>
        /// Read a column as a string, or null for DBNull.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Value.</returns>
        protected static string? ReadString(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            object value = reader.GetValue(ordinal);
            if (value is byte[] bytes) return System.Text.Encoding.UTF8.GetString(bytes);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a numeric column as a 64-bit integer, or null for DBNull or values that do not fit.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Value.</returns>
        protected static long? ReadLong(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return null;
            object value = reader.GetValue(ordinal);
            try
            {
                if (value is string s) return Int64.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Read a flag column stored as a boolean, a number, or YES/NO text.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="ordinal">Column ordinal.</param>
        /// <returns>Value; false for DBNull.</returns>
        protected static bool ReadBool(DbDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal)) return false;
            object value = reader.GetValue(ordinal);
            if (value is bool b) return b;
            if (value is string s) return s == "1" || s.Equals("YES", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private async Task PopulateAsync(DbConnection connection, DbTransaction? transaction, TableDetail table, CancellationToken token)
        {
            string schema = table.SchemaName;
            string name = table.TableName;

            List<ColumnDetail> columns = new List<ColumnDetail>();
            using (DbCommand command = CreateCommand(connection, transaction, ColumnsSql, schema, name))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        columns.Add(new ColumnDetail
                        {
                            ColumnName = ReadString(reader, 0) ?? String.Empty,
                            DataType = ReadString(reader, 1) ?? String.Empty,
                            IsNullable = ReadBool(reader, 2),
                            DefaultValue = ReadString(reader, 3),
                            MaxLength = ReadLong(reader, 4),
                            Ordinal = (int)(ReadLong(reader, 5) ?? 0)
                        });
                    }
                }
            }
            table.Columns = columns.OrderBy(c => c.Ordinal).ToList();

            HashSet<string> primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (DbCommand command = CreateCommand(connection, transaction, PrimaryKeysSql, schema, name))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string? column = ReadString(reader, 0);
                        if (column != null) primaryKeys.Add(column);
                    }
                }
            }
            foreach (ColumnDetail column in table.Columns)
            {
                if (primaryKeys.Contains(column.ColumnName)) column.IsPrimaryKey = true;
            }

            List<ForeignKeyDetail> foreignKeys = new List<ForeignKeyDetail>();
            using (DbCommand command = CreateCommand(connection, transaction, ForeignKeysSql, schema, name))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        foreignKeys.Add(new ForeignKeyDetail
                        {
                            ConstraintName = ReadString(reader, 0) ?? String.Empty,
                            ColumnName = ReadString(reader, 1) ?? String.Empty,
                            ReferencedSchema = ReadString(reader, 2),
                            ReferencedTable = ReadString(reader, 3) ?? String.Empty,
                            ReferencedColumn = ReadString(reader, 4)
                        });
                    }
                }
            }
            table.ForeignKeys = foreignKeys;

            List<IndexDetail> indexes = new List<IndexDetail>();
            Dictionary<string, IndexDetail> byName = new Dictionary<string, IndexDetail>(StringComparer.Ordinal);
            using (DbCommand command = CreateCommand(connection, transaction, IndexesSql, schema, name))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string indexName = ReadString(reader, 0) ?? String.Empty;
                        if (!byName.TryGetValue(indexName, out IndexDetail? index))
                        {
                            index = new IndexDetail
                            {
                                IndexName = indexName,
                                IsUnique = ReadBool(reader, 2),
                                IsPrimary = ReadBool(reader, 3)
                            };
                            byName[indexName] = index;
                            indexes.Add(index);
                        }
                        index.Columns.Add(ReadString(reader, 1) ?? "(expression)");
                    }
                }
            }
            table.Indexes = indexes;

            table.RowEstimate = await ReadRowEstimateAsync(connection, transaction, table, token).ConfigureAwait(false);
        }

        private static string NormalizeTableType(string? type)
        {
            if (String.IsNullOrEmpty(type)) return "BASE TABLE";
            string upper = type.Trim().ToUpperInvariant();
            if (upper == "TABLE" || upper == "BASE TABLE") return "BASE TABLE";
            return upper;
        }

        #endregion
    }
}
