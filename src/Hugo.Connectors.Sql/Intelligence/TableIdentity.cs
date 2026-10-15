// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Helpers/TableIdentity.cs and the table lookups in src/Tablix.Server/Handlers/ChatHandler.cs
// (FindTablesByName). Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License. Modified for Hugo: the table
// identity is the knowledge key schema.table (Tablix minted tbl_ identifiers for its own database); lookups accept
// qualified or bare names. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Table identity used for knowledge keys (<c>schema.table</c>) and name lookups against a crawl. Thread-safe.
    /// </summary>
    public static class TableIdentity
    {
        #region Public-Methods

        /// <summary>
        /// Knowledge key of a table: <c>schema.table</c>, or the bare table name when the schema is empty.
        /// </summary>
        /// <param name="schemaName">Schema name.</param>
        /// <param name="tableName">Table name.</param>
        /// <returns>Key.</returns>
        public static string Key(string? schemaName, string? tableName)
        {
            if (String.IsNullOrWhiteSpace(schemaName)) return tableName ?? String.Empty;
            return schemaName + "." + (tableName ?? String.Empty);
        }

        /// <summary>
        /// Knowledge key of a crawled table.
        /// </summary>
        /// <param name="table">Table.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when table is null.</exception>
        public static string Key(TableDetail table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return Key(table.SchemaName, table.TableName);
        }

        /// <summary>
        /// Find tables by name. Accepts <c>schema.table</c> or a bare name, case-insensitively. A bare name that
        /// exists in several schemas returns every match.
        /// </summary>
        /// <param name="detail">Crawl.</param>
        /// <param name="name">Table name, optionally schema-qualified.</param>
        /// <returns>Matches; empty when none.</returns>
        public static List<TableDetail> Find(SchemaDetail? detail, string? name)
        {
            List<TableDetail> matches = new List<TableDetail>();
            if (detail == null || String.IsNullOrWhiteSpace(name)) return matches;
            string trimmed = name.Trim();

            TableDetail? exact = detail.Tables.FirstOrDefault(t => String.Equals(Key(t), trimmed, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                matches.Add(exact);
                return matches;
            }

            string? schema = null;
            string table = trimmed;
            int dot = trimmed.IndexOf('.');
            if (dot > 0 && dot < trimmed.Length - 1)
            {
                schema = trimmed.Substring(0, dot);
                table = trimmed.Substring(dot + 1);
            }

            foreach (TableDetail t in detail.Tables)
            {
                bool nameMatches = String.Equals(t.TableName, table, StringComparison.OrdinalIgnoreCase);
                bool schemaMatches = schema == null || String.Equals(t.SchemaName, schema, StringComparison.OrdinalIgnoreCase);
                if (nameMatches && schemaMatches) matches.Add(t);
            }
            return matches;
        }

        #endregion
    }
}
