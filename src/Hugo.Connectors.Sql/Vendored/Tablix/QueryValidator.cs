// Vendored from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Helpers/QueryValidator.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, braces and naming per Hugo style. The allow-list validation is
// kept for reference and for statement type labels; Hugo's effect classification lives in StatementClassifier, which
// extends this keyword-first approach with a dialect-aware tokenizer. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Vendored.Tablix
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Validates SQL queries against allowed statement types and derives a bounded statement type label. Thread-safe.
    /// </summary>
    public static class QueryValidator
    {
        #region Private-Members

        private static readonly HashSet<string> _StatementTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "select", "insert", "update", "delete", "merge", "create", "alter", "drop", "truncate",
            "replace", "pragma", "explain", "show", "describe", "exec", "call"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a SQL query against a list of allowed statement types.
        /// </summary>
        /// <param name="query">SQL query string.</param>
        /// <param name="allowedQueries">Allowed statement types (for example SELECT, INSERT).</param>
        /// <returns>Null when valid, or an error message.</returns>
        public static string? Validate(string? query, List<string>? allowedQueries)
        {
            if (String.IsNullOrWhiteSpace(query)) return "Query cannot be empty.";
            if (allowedQueries == null || allowedQueries.Count == 0) return "No query types are permitted for this database.";

            query = NormalizeSingleStatement(query);
            if (query.Contains(';')) return "Multi-statement queries are not supported. Remove semicolons from your query.";

            string stripped = StripLeadingComments(query).TrimStart();
            if (String.IsNullOrWhiteSpace(stripped)) return "Query is empty after removing comments.";

            string? firstWord = GetEffectiveStatementKeyword(stripped);
            if (String.IsNullOrEmpty(firstWord)) return "Unable to determine query type.";

            string normalizedKeyword = firstWord.ToUpperInvariant();
            bool isAllowed = allowedQueries.Any(a => String.Equals(a, normalizedKeyword, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed) return "Query type '" + normalizedKeyword + "' is not permitted. Allowed types: " + String.Join(", ", allowedQueries) + ".";
            return null;
        }

        /// <summary>
        /// Classify a SQL query into a bounded, lower-case statement type suitable for a metric label. Known keywords are
        /// returned as-is; anything else, including empty or unparseable input, returns <c>other</c>. Never throws.
        /// </summary>
        /// <param name="query">SQL query string. May be null.</param>
        /// <returns>Lower-case statement type, or <c>other</c>.</returns>
        public static string GetStatementType(string? query)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(query)) return "other";
                string stripped = StripLeadingComments(NormalizeSingleStatement(query)).TrimStart();
                if (String.IsNullOrWhiteSpace(stripped)) return "other";
                string? keyword = GetEffectiveStatementKeyword(stripped);
                if (String.IsNullOrEmpty(keyword)) return "other";
                string normalized = keyword.ToLowerInvariant();
                return _StatementTypes.Contains(normalized) ? normalized : "other";
            }
            catch (Exception)
            {
                return "other";
            }
        }

        /// <summary>
        /// Remove a single trailing statement terminator when the text contains no other semicolon.
        /// </summary>
        /// <param name="query">SQL query string.</param>
        /// <returns>Normalized SQL query.</returns>
        public static string NormalizeSingleStatement(string query)
        {
            if (String.IsNullOrWhiteSpace(query)) return query;
            string normalized = query.Trim();
            if (!normalized.EndsWith(";", StringComparison.Ordinal)) return normalized;
            string withoutTerminator = normalized.Substring(0, normalized.Length - 1).TrimEnd();
            return withoutTerminator.Contains(';') ? normalized : withoutTerminator;
        }

        #endregion

        #region Private-Methods

        private static string? GetEffectiveStatementKeyword(string sql)
        {
            string? firstWord = ReadWord(sql, 0, out int nextIndex);
            if (!String.Equals(firstWord, "WITH", StringComparison.OrdinalIgnoreCase)) return firstWord;

            int index = nextIndex;
            SkipWhitespace(sql, ref index);
            string? maybeRecursive = ReadWord(sql, index, out int afterMaybeRecursive);
            if (String.Equals(maybeRecursive, "RECURSIVE", StringComparison.OrdinalIgnoreCase)) index = afterMaybeRecursive;

            while (index < sql.Length)
            {
                SkipWhitespace(sql, ref index);
                string? cteName = ReadWord(sql, index, out index);
                if (String.IsNullOrEmpty(cteName)) return firstWord;

                SkipWhitespace(sql, ref index);
                if (index < sql.Length && sql[index] == '(')
                {
                    index = FindMatchingParen(sql, index);
                    if (index < 0) return firstWord;
                    index++;
                }

                SkipWhitespace(sql, ref index);
                string? asKeyword = ReadWord(sql, index, out index);
                if (!String.Equals(asKeyword, "AS", StringComparison.OrdinalIgnoreCase)) return firstWord;

                SkipWhitespace(sql, ref index);
                if (index >= sql.Length || sql[index] != '(') return firstWord;

                index = FindMatchingParen(sql, index);
                if (index < 0) return firstWord;
                index++;

                SkipWhitespace(sql, ref index);
                if (index < sql.Length && sql[index] == ',')
                {
                    index++;
                    continue;
                }

                return ReadWord(sql, index, out _);
            }

            return firstWord;
        }

        private static string? ReadWord(string sql, int startIndex, out int nextIndex)
        {
            nextIndex = startIndex;
            if (String.IsNullOrEmpty(sql)) return null;
            SkipWhitespace(sql, ref nextIndex);
            int wordStart = nextIndex;
            while (nextIndex < sql.Length && (Char.IsLetterOrDigit(sql[nextIndex]) || sql[nextIndex] == '_')) nextIndex++;
            return nextIndex == wordStart ? null : sql.Substring(wordStart, nextIndex - wordStart);
        }

        private static void SkipWhitespace(string sql, ref int index)
        {
            while (index < sql.Length && Char.IsWhiteSpace(sql[index])) index++;
        }

        private static int FindMatchingParen(string sql, int openParenIndex)
        {
            int depth = 0;
            for (int i = openParenIndex; i < sql.Length; i++)
            {
                char ch = sql[i];
                if (ch == '\'')
                {
                    i = SkipQuoted(sql, i, '\'');
                    continue;
                }
                if (ch == '"')
                {
                    i = SkipQuoted(sql, i, '"');
                    continue;
                }
                if (ch == '[')
                {
                    int closeIndex = sql.IndexOf(']', i + 1);
                    i = closeIndex < 0 ? sql.Length - 1 : closeIndex;
                    continue;
                }
                if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    int newlineIndex = sql.IndexOf('\n', i + 2);
                    i = newlineIndex < 0 ? sql.Length - 1 : newlineIndex;
                    continue;
                }
                if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int endIndex = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = endIndex < 0 ? sql.Length - 1 : endIndex + 1;
                    continue;
                }
                if (ch == '(')
                {
                    depth++;
                    continue;
                }
                if (ch == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static int SkipQuoted(string sql, int quoteIndex, char quote)
        {
            for (int i = quoteIndex + 1; i < sql.Length; i++)
            {
                if (sql[i] != quote) continue;
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i++;
                    continue;
                }
                return i;
            }
            return sql.Length - 1;
        }

        private static string StripLeadingComments(string sql)
        {
            string result = sql;
            while (true)
            {
                result = result.TrimStart();
                if (result.StartsWith("--", StringComparison.Ordinal))
                {
                    int newlineIndex = result.IndexOf('\n');
                    if (newlineIndex < 0) return String.Empty;
                    result = result.Substring(newlineIndex + 1);
                    continue;
                }
                if (result.StartsWith("/*", StringComparison.Ordinal))
                {
                    int endIndex = result.IndexOf("*/", StringComparison.Ordinal);
                    if (endIndex < 0) return String.Empty;
                    result = result.Substring(endIndex + 2);
                    continue;
                }
                break;
            }
            return result;
        }

        #endregion
    }
}
