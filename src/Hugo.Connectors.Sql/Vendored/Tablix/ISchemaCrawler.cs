// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/DatabaseDrivers/IDatabaseCrawler.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed to ISchemaCrawler; crawlers take an already open connection (and optional transaction)
// instead of building their own connection strings; query execution and connection tests moved to the connector;
// added ListTablesAsync and DescribeTableAsync. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads schema geometry (tables, columns, keys, indexes) from an open connection. Implementations are stateless and
    /// thread-safe; the connection itself is not.
    /// </summary>
    public interface ISchemaCrawler
    {
        /// <summary>
        /// List the tables and views in a schema, without columns.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="schema">Schema name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tables ordered by name.</returns>
        Task<List<TableDetail>> ListTablesAsync(DbConnection connection, DbTransaction? transaction, string schema, CancellationToken token = default);

        /// <summary>
        /// Describe one table or view: columns, primary key, foreign keys, indexes, and row estimate.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="schema">Schema name.</param>
        /// <param name="tableName">Table or view name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The table, or null when it does not exist.</returns>
        Task<TableDetail?> DescribeTableAsync(DbConnection connection, DbTransaction? transaction, string schema, string tableName, CancellationToken token = default);

        /// <summary>
        /// Crawl every table and view in a schema.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="transaction">Active transaction, or null.</param>
        /// <param name="schema">Schema name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Schema detail.</returns>
        Task<SchemaDetail> CrawlAsync(DbConnection connection, DbTransaction? transaction, string schema, CancellationToken token = default);
    }
}
