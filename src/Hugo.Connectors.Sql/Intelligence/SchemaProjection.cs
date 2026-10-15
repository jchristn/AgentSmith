// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Helpers/SchemaProjection.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace and style; opaque cursor instead of skip; table summaries carry type, row estimate,
// primary key, key summary, and a context marker from the knowledge store; search also matches column names;
// relationships can be filtered by table (either end) and minimum confidence, and include inferred candidates by
// default (Tablix's MCP tool ignored includeInferred). See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Projects a crawl into compact, paged, model-facing indexes. Thread-safe.
    /// </summary>
    public static class SchemaProjection
    {
        #region Public-Methods

        /// <summary>
        /// One page of the compact table index, ordered by schema then table.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="knowledge">Durable context, or null.</param>
        /// <param name="pageSize">Page size, clamped to 1 to 1000.</param>
        /// <param name="cursor">Cursor from a previous page, or null for the first page.</param>
        /// <param name="search">Case-insensitive text matched against schema, table, and column names, or null.</param>
        /// <param name="schema">Exact schema filter, or null.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detail is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for a malformed cursor.</exception>
        public static SchemaPage<TableSummary> CreateTableListResult(SchemaDetail detail, SchemaKnowledge? knowledge, int pageSize, string? cursor, string? search = null, string? schema = null)
        {
            ArgumentNullException.ThrowIfNull(detail);
            SchemaKnowledge k = knowledge ?? new SchemaKnowledge();
            int size = Math.Clamp(pageSize, 1, 1000);
            int offset = ParseCursor(cursor);

            IEnumerable<TableDetail> tables = detail.Tables
                .OrderBy(t => t.SchemaName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TableName, StringComparer.OrdinalIgnoreCase);
            if (!String.IsNullOrWhiteSpace(schema))
                tables = tables.Where(t => String.Equals(t.SchemaName, schema.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!String.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                tables = tables.Where(t => Contains(t.SchemaName, s) || Contains(t.TableName, s) || Contains(TableIdentity.Key(t), s) || t.Columns.Any(c => Contains(c.ColumnName, s)));
            }

            List<TableDetail> filtered = tables.ToList();
            SchemaPage<TableSummary> page = NewPage<TableSummary>(detail, filtered.Count, size, offset, search);
            page.Items = filtered.Skip(offset).Take(size).Select(t => Summarize(t, k)).ToList();
            Finish(page, offset);
            return page;
        }

        /// <summary>
        /// One page of the relationship index, ordered by source table and column.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="relationships">Relationships (declared and inferred) built from the crawl.</param>
        /// <param name="pageSize">Page size, clamped to 1 to 1000.</param>
        /// <param name="cursor">Cursor from a previous page, or null.</param>
        /// <param name="search">Case-insensitive text matched against tables, columns, and constraint names, or null.</param>
        /// <param name="table">Table (bare or schema-qualified) that must be at either end, or null.</param>
        /// <param name="includeInferred">Whether inferred candidates are included.</param>
        /// <param name="minConfidence">Minimum confidence, 0 to 1.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when detail or relationships is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for a malformed cursor.</exception>
        public static SchemaPage<RelationshipDetail> CreateRelationshipListResult(
            SchemaDetail detail,
            List<RelationshipDetail> relationships,
            int pageSize,
            string? cursor,
            string? search = null,
            string? table = null,
            bool includeInferred = true,
            double minConfidence = 0)
        {
            ArgumentNullException.ThrowIfNull(detail);
            ArgumentNullException.ThrowIfNull(relationships);
            int size = Math.Clamp(pageSize, 1, 1000);
            int offset = ParseCursor(cursor);

            IEnumerable<RelationshipDetail> edges = relationships;
            if (!includeInferred) edges = edges.Where(r => r.IsDeclared);
            if (minConfidence > 0) edges = edges.Where(r => r.Confidence >= minConfidence);
            if (!String.IsNullOrWhiteSpace(table))
            {
                string t = table.Trim();
                edges = edges.Where(r => MatchesTable(r.FromSchema, r.FromTable, t) || MatchesTable(r.ToSchema, r.ToTable, t));
            }
            if (!String.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                edges = edges.Where(r => Contains(r.FromSchema, s) || Contains(r.FromTable, s) || Contains(r.FromColumn, s)
                    || Contains(r.ToSchema, s) || Contains(r.ToTable, s) || Contains(r.ToColumn, s) || Contains(r.ConstraintName, s));
            }

            List<RelationshipDetail> filtered = edges.ToList();
            SchemaPage<RelationshipDetail> page = NewPage<RelationshipDetail>(detail, filtered.Count, size, offset, search);
            page.Table = String.IsNullOrWhiteSpace(table) ? null : table.Trim();
            page.Items = filtered.Skip(offset).Take(size).ToList();
            Finish(page, offset);
            return page;
        }

        /// <summary>
        /// Summarize one table for the compact index.
        /// </summary>
        /// <param name="table">Table.</param>
        /// <param name="knowledge">Durable context, or null.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when table is null.</exception>
        public static TableSummary Summarize(TableDetail table, SchemaKnowledge? knowledge)
        {
            ArgumentNullException.ThrowIfNull(table);
            ResourceKnowledgeEntry? entry = knowledge?.Table(table.SchemaName, table.TableName);
            List<string> pk = table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();
            List<string> parts = new List<string>();
            if (pk.Count > 0) parts.Add("PK " + String.Join(",", pk));
            foreach (ForeignKeyDetail fk in table.ForeignKeys.Take(6))
                parts.Add("FK " + fk.ColumnName + "->" + fk.ReferencedTable);
            if (table.ForeignKeys.Count > 6) parts.Add("+" + (table.ForeignKeys.Count - 6) + " FK");

            return new TableSummary
            {
                SchemaName = table.SchemaName,
                TableName = table.TableName,
                TableType = table.TableType,
                RowEstimate = table.RowEstimate,
                Columns = table.Columns.Count,
                ForeignKeys = table.ForeignKeys.Count,
                Indexes = table.Indexes.Count,
                PrimaryKey = pk,
                KeySummary = String.Join("; ", parts),
                HasContext = entry != null,
                ContextSource = entry?.Source.ToString()
            };
        }

        /// <summary>
        /// Render a table summary as one index line.
        /// </summary>
        /// <param name="summary">Summary.</param>
        /// <returns>Line without a trailing newline.</returns>
        public static string RenderLine(TableSummary summary)
        {
            ArgumentNullException.ThrowIfNull(summary);
            string line = TableIdentity.Key(summary.SchemaName, summary.TableName);
            if (!String.Equals(summary.TableType, "BASE TABLE", StringComparison.OrdinalIgnoreCase)) line += " [" + summary.TableType.ToLowerInvariant() + "]";
            line += " | ~" + (summary.RowEstimate.HasValue ? summary.RowEstimate.Value.ToString(CultureInfo.InvariantCulture) : "?") + " rows | " + summary.Columns + " cols";
            if (summary.KeySummary.Length > 0) line += " | " + summary.KeySummary;
            if (summary.HasContext) line += " | context (" + summary.ContextSource + ")";
            return line;
        }

        /// <summary>
        /// Render a relationship as one index line.
        /// </summary>
        /// <param name="r">Relationship.</param>
        /// <returns>Line without a trailing newline.</returns>
        public static string RenderLine(RelationshipDetail r)
        {
            ArgumentNullException.ThrowIfNull(r);
            string line = TableIdentity.Key(r.FromSchema, r.FromTable) + "." + r.FromColumn + " -> " + TableIdentity.Key(r.ToSchema, r.ToTable) + "." + (r.ToColumn ?? "(primary key)");
            if (r.IsDeclared) return line + " | declared" + (r.ConstraintName == null ? String.Empty : " " + r.ConstraintName);
            line += " | inferred " + r.Confidence.ToString("0.00", CultureInfo.InvariantCulture);
            List<string> evidence = r.Evidence.Where(e => !e.StartsWith("No declared foreign key", StringComparison.Ordinal)).Take(3).ToList();
            if (evidence.Count > 0) line += " | " + String.Join(" ", evidence);
            return line;
        }

        /// <summary>
        /// Parse a cursor (the offset of the first item, as returned in <c>nextCursor</c>).
        /// </summary>
        /// <param name="cursor">Cursor or null.</param>
        /// <returns>Offset, zero for none.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when malformed.</exception>
        public static int ParseCursor(string? cursor)
        {
            if (String.IsNullOrWhiteSpace(cursor)) return 0;
            if (Int32.TryParse(cursor.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset) && offset >= 0) return offset;
            throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Cursor '" + cursor + "' is not valid; pass the nextCursor value from the previous page, or omit it for the first page.");
        }

        #endregion

        #region Private-Methods

        private static SchemaPage<T> NewPage<T>(SchemaDetail detail, int total, int size, int offset, string? search)
        {
            return new SchemaPage<T>
            {
                Schema = detail.Schema,
                Search = String.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                TableCount = detail.Tables.Count,
                TotalRecords = total,
                PageSize = size,
                Cursor = offset == 0 ? String.Empty : offset.ToString(CultureInfo.InvariantCulture),
                CrawledUtc = detail.CrawledUtc
            };
        }

        private static void Finish<T>(SchemaPage<T> page, int offset)
        {
            int next = offset + page.Items.Count;
            page.EndOfResults = next >= page.TotalRecords;
            page.NextCursor = page.EndOfResults ? null : next.ToString(CultureInfo.InvariantCulture);
        }

        private static bool MatchesTable(string? schema, string? table, string filter)
        {
            if (String.Equals(table, filter, StringComparison.OrdinalIgnoreCase)) return true;
            return String.Equals(TableIdentity.Key(schema, table), filter, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string? value, string filter)
        {
            return value != null && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
