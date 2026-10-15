// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6, the
// private name helpers of src/Tablix.Core/Helpers/DatabaseIntelligenceBuilder.cs (NormalizeIdentifier, Singularize,
// ContainsAny, IsLikelyForeignKey, IsMetricColumn, IsStatusColumn, IsActivityColumn, IsTenantColumn,
// IsSoftDeleteColumn, IsFreshnessColumn). Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: made public for reuse by sql_ask; word-level matching instead of raw substrings for role and
// filter keywords (so "catalog" is not an event table and "statement" is not a status column); foreign key names
// need a word boundary before "id" (so "paid" and "uuid" are not keys); freshness accepts text-typed timestamps with
// temporal names (SQLite) and excludes soft-delete columns; type families for join compatibility.
// See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Name and type heuristics behind relationship inference, domain intelligence, and ambiguity detection.
    /// Thread-safe.
    /// </summary>
    public static class NameHeuristics
    {
        #region Public-Methods

        /// <summary>
        /// Lower-case letters and digits only (for example <c>Customer_ID</c> becomes <c>customerid</c>).
        /// </summary>
        /// <param name="value">Identifier.</param>
        /// <returns>Normalized text; empty for null.</returns>
        public static string NormalizeIdentifier(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (Char.IsLetterOrDigit(character)) builder.Append(Char.ToLowerInvariant(character));
            }
            return builder.ToString();
        }

        /// <summary>
        /// Naive English singular (<c>categories</c> to <c>category</c>, <c>addresses</c> to <c>address</c>,
        /// <c>orders</c> to <c>order</c>).
        /// </summary>
        /// <param name="value">Lower-case word.</param>
        /// <returns>Singular form.</returns>
        public static string Singularize(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return value ?? String.Empty;
            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 3) return value.Substring(0, value.Length - 3) + "y";
            if (value.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && value.Length > 3) return value.Substring(0, value.Length - 2);
            if (value.EndsWith("ss", StringComparison.OrdinalIgnoreCase)) return value;
            if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 1) return value.Substring(0, value.Length - 1);
            return value;
        }

        /// <summary>
        /// Split an identifier into lower-case words on underscores, hyphens, spaces, dots, and camel-case boundaries
        /// (<c>legacy_accounts</c> and <c>legacyAccounts</c> both become <c>legacy</c>, <c>accounts</c>).
        /// </summary>
        /// <param name="value">Identifier.</param>
        /// <returns>Words; empty for null.</returns>
        public static List<string> Words(string? value)
        {
            List<string> words = new List<string>();
            if (String.IsNullOrWhiteSpace(value)) return words;
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!Char.IsLetterOrDigit(c))
                {
                    Flush(words, current);
                    continue;
                }
                if (Char.IsUpper(c) && current.Length > 0 && i > 0 && Char.IsLower(value[i - 1])) Flush(words, current);
                current.Append(Char.ToLowerInvariant(c));
            }
            Flush(words, current);
            return words;
        }

        /// <summary>
        /// Tablix substring test: whether the identifier contains any needle, raw or normalized.
        /// </summary>
        /// <param name="value">Identifier.</param>
        /// <param name="needles">Needles.</param>
        /// <returns>True on a match.</returns>
        public static bool ContainsAny(string? value, params string[] needles)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            string normalizedValue = NormalizeIdentifier(value);
            foreach (string needle in needles)
            {
                if (String.IsNullOrWhiteSpace(needle)) continue;
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
                string normalizedNeedle = NormalizeIdentifier(needle);
                if (normalizedNeedle.Length > 0 && normalizedValue.Contains(normalizedNeedle, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether any word of the identifier (or its singular) equals one of the given words. A word that merely ends
        /// with a candidate of five or more letters also matches, so unseparated names such as <c>orderstatus</c>
        /// still count.
        /// </summary>
        /// <param name="value">Identifier.</param>
        /// <param name="candidates">Lower-case words.</param>
        /// <returns>True on a match.</returns>
        public static bool HasWord(string? value, params string[] candidates)
        {
            foreach (string word in Words(value))
            {
                string singular = Singularize(word);
                if (candidates.Any(c => c == word || c == singular)) return true;
                if (candidates.Any(c => c.Length >= 5 && word.Length > c.Length && (word.EndsWith(c, StringComparison.Ordinal) || singular.EndsWith(c, StringComparison.Ordinal)))) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a column name looks like a foreign key: it ends in <c>_id</c>, <c>Id</c>, or <c>ID</c> after a
        /// word, and is not just <c>id</c>.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when key-like.</returns>
        public static bool IsLikelyForeignKey(string? columnName)
        {
            if (String.IsNullOrWhiteSpace(columnName) || columnName.Length < 3) return false;
            if (columnName.EndsWith("_id", StringComparison.OrdinalIgnoreCase) || columnName.EndsWith("-id", StringComparison.OrdinalIgnoreCase))
                return NormalizeIdentifier(columnName).Length > 2;
            if (columnName.EndsWith("Id", StringComparison.Ordinal) || columnName.EndsWith("ID", StringComparison.Ordinal))
                return Char.IsLower(columnName[columnName.Length - 3]) || columnName[columnName.Length - 3] == '_';
            return false;
        }

        /// <summary>
        /// The entity a key-like column points at (<c>customer_id</c> gives <c>customer</c>), or null.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>Normalized entity token, or null.</returns>
        public static string? ForeignKeyEntityToken(string? columnName)
        {
            if (!IsLikelyForeignKey(columnName)) return null;
            string normalized = NormalizeIdentifier(columnName);
            if (normalized.Length <= 2 || !normalized.EndsWith("id", StringComparison.Ordinal)) return null;
            return normalized.Substring(0, normalized.Length - 2);
        }

        /// <summary>
        /// Type family for join compatibility: <c>number</c>, <c>text</c>, <c>uuid</c>, <c>date</c>, <c>binary</c>,
        /// <c>boolean</c>, or empty when unknown.
        /// </summary>
        /// <param name="dataType">Engine data type.</param>
        /// <returns>Family.</returns>
        public static string TypeFamily(string? dataType)
        {
            if (String.IsNullOrWhiteSpace(dataType)) return String.Empty;
            string t = dataType.ToLowerInvariant();
            if (t.Contains("uuid", StringComparison.Ordinal) || t.Contains("uniqueidentifier", StringComparison.Ordinal)) return "uuid";
            if (t.Contains("bool", StringComparison.Ordinal) || t == "bit") return "boolean";
            if (t.Contains("date", StringComparison.Ordinal) || t.Contains("time", StringComparison.Ordinal) || t.Contains("interval", StringComparison.Ordinal)) return "date";
            if (t.Contains("int", StringComparison.Ordinal) || t.Contains("serial", StringComparison.Ordinal) || t.Contains("dec", StringComparison.Ordinal)
                || t.Contains("numeric", StringComparison.Ordinal) || t.Contains("number", StringComparison.Ordinal) || t.Contains("real", StringComparison.Ordinal)
                || t.Contains("double", StringComparison.Ordinal) || t.Contains("float", StringComparison.Ordinal) || t.Contains("money", StringComparison.Ordinal))
                return "number";
            if (t.Contains("char", StringComparison.Ordinal) || t.Contains("text", StringComparison.Ordinal) || t.Contains("string", StringComparison.Ordinal)
                || t.Contains("clob", StringComparison.Ordinal) || t.Contains("enum", StringComparison.Ordinal) || t.Contains("citext", StringComparison.Ordinal))
                return "text";
            if (t.Contains("blob", StringComparison.Ordinal) || t.Contains("binary", StringComparison.Ordinal) || t.Contains("bytea", StringComparison.Ordinal)) return "binary";
            return String.Empty;
        }

        /// <summary>
        /// Whether two column types can hold the same key values. Unknown types are compatible; UUID and text are
        /// compatible because UUIDs are often stored as text.
        /// </summary>
        /// <param name="left">Data type.</param>
        /// <param name="right">Data type.</param>
        /// <returns>True when compatible.</returns>
        public static bool TypesCompatible(string? left, string? right)
        {
            string a = TypeFamily(left);
            string b = TypeFamily(right);
            if (a.Length == 0 || b.Length == 0 || a == b) return true;
            return (a == "uuid" && b == "text") || (a == "text" && b == "uuid");
        }

        /// <summary>
        /// Whether a column is a likely metric (an amount, total, price, cost, quantity, or balance with a numeric
        /// type). Key-like columns are never metrics.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <param name="dataType">Data type.</param>
        /// <returns>True when metric-like.</returns>
        public static bool IsMetricColumn(string? columnName, string? dataType)
        {
            if (IsLikelyForeignKey(columnName)) return false;
            if (!ContainsAny(columnName, "amount", "total", "price", "cost", "revenue", "quantity", "qty", "balance", "subtotal", "fee", "tax", "discount", "sales")) return false;
            if (String.IsNullOrWhiteSpace(dataType)) return true;
            return TypeFamily(dataType) == "number";
        }

        /// <summary>
        /// Natural aggregations for a metric column, with an optional caveat.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <param name="note">Caveat, or null.</param>
        /// <returns>Aggregations.</returns>
        public static List<string> MetricAggregations(string? columnName, out string? note)
        {
            note = null;
            if (ContainsAny(columnName, "price", "rate") || HasWord(columnName, "unit"))
            {
                note = "A per-unit value; multiply by a quantity before summing.";
                return new List<string> { "AVG", "MIN", "MAX" };
            }
            if (ContainsAny(columnName, "balance"))
            {
                note = "A running balance; take the latest value per entity instead of summing rows.";
                return new List<string> { "MAX", "AVG" };
            }
            if (ContainsAny(columnName, "quantity", "qty")) return new List<string> { "SUM", "AVG" };
            return new List<string> { "SUM", "AVG", "MIN", "MAX" };
        }

        /// <summary>
        /// Whether a column holds a status, state, stage, or phase.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when status-like.</returns>
        public static bool IsStatusColumn(string? columnName)
        {
            return HasWord(columnName, "status", "state", "stage", "phase");
        }

        /// <summary>
        /// Whether a column is an activity flag (active, enabled, disabled).
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when activity-like.</returns>
        public static bool IsActivityColumn(string? columnName)
        {
            return HasWord(columnName, "active", "inactive", "enabled", "disabled");
        }

        /// <summary>
        /// Whether a column classifies rows (type, kind, category).
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when type-like.</returns>
        public static bool IsTypeColumn(string? columnName)
        {
            return HasWord(columnName, "type", "kind", "category");
        }

        /// <summary>
        /// Whether a column scopes rows to a tenant or account.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when tenant-like.</returns>
        public static bool IsTenantColumn(string? columnName)
        {
            string n = NormalizeIdentifier(columnName);
            return n == "tenantid" || n == "accountid" || n == "orgid" || n == "organizationid" || n == "companyid" || n == "workspaceid";
        }

        /// <summary>
        /// Whether a column marks soft deletion or archiving.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when soft-delete-like.</returns>
        public static bool IsSoftDeleteColumn(string? columnName)
        {
            return HasWord(columnName, "deleted", "archived", "removed");
        }

        /// <summary>
        /// Whether a column records when a row was created, changed, or happened. Soft-delete timestamps are excluded.
        /// Text-typed columns (SQLite stores timestamps as text) count only when the last word is temporal
        /// (<c>_at</c>, <c>_on</c>, <c>date</c>, <c>time</c>, <c>timestamp</c>).
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <param name="dataType">Data type, or null to judge by name only.</param>
        /// <returns>True when freshness-like.</returns>
        public static bool IsFreshnessColumn(string? columnName, string? dataType)
        {
            List<string> words = Words(columnName);
            if (words.Count == 0 || IsSoftDeleteColumn(columnName)) return false;
            string last = words[words.Count - 1];
            bool temporalLast = (words.Count > 1 && (last == "at" || last == "on")) || last == "date" || last == "time" || last == "timestamp" || last == "datetime" || last == "ts";
            bool temporalAny = temporalLast || words.Any(w => w == "date" || w == "timestamp" || w == "datetime") || ContainsAny(columnName, "created_at", "updated_at", "created_on", "updated_on");
            if (!temporalAny) return false;
            if (String.IsNullOrWhiteSpace(dataType)) return true;
            string family = TypeFamily(dataType);
            if (family == "date") return true;
            return family == "text" && temporalLast;
        }

        /// <summary>
        /// Whether a column names an owner or assignee.
        /// </summary>
        /// <param name="columnName">Column name.</param>
        /// <returns>True when ownership-like.</returns>
        public static bool IsOwnerColumn(string? columnName)
        {
            string n = NormalizeIdentifier(columnName);
            return HasWord(columnName, "owner", "assignee") || n == "createdby" || n == "updatedby" || n == "assignedto";
        }

        /// <summary>
        /// Whether a table name names a party (customer, client, user, account, member, person).
        /// </summary>
        /// <param name="tableName">Table name.</param>
        /// <returns>True when party-like.</returns>
        public static bool IsPartyTable(string? tableName)
        {
            return HasWord(tableName, "customer", "client", "user", "account", "member", "person", "people");
        }

        #endregion

        #region Private-Methods

        private static void Flush(List<string> words, StringBuilder current)
        {
            if (current.Length == 0) return;
            words.Add(current.ToString());
            current.Clear();
        }

        #endregion
    }
}
