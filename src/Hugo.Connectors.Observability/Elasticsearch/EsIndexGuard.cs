namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Enforces a resource's index allow list. An index expression is a comma-separated list of names or <c>*</c>
    /// globs. Each part must be well formed (lowercase letters, digits, <c>.</c>, <c>_</c>, <c>-</c>, <c>*</c>; no
    /// <c>_all</c>, exclusions, remote clusters, or date math) and covered by an allowed pattern. Coverage treats the
    /// requested part's <c>*</c> characters as literals and glob-matches the whole part against the allowed pattern,
    /// which is sound for <c>*</c>-only globs: every index the part can expand to also matches the pattern. Hidden
    /// indexes (a leading dot) are covered only by patterns that also start with a dot. Thread-safe.
    /// </summary>
    public static class EsIndexGuard
    {
        #region Private-Members

        private static readonly Regex _Part = new Regex("^[a-z0-9.*][a-z0-9._*-]*$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check an index expression.
        /// </summary>
        /// <param name="expression">Expression, for example <c>logs-2026.10,metrics-*</c>.</param>
        /// <param name="allowed">Allowed patterns. Empty allows every index not starting with a dot.</param>
        /// <returns>Outcome.</returns>
        public static EsIndexCheck Check(string? expression, IList<string>? allowed)
        {
            IList<string> patterns = allowed ?? new List<string>();
            if (String.IsNullOrWhiteSpace(expression))
                return Refuse(ConnectorErrorCodes.InvalidArgument, "An index or index pattern is required.");

            List<string> parts = expression.Split(',').Select(p => p.Trim()).ToList();
            List<string> accepted = new List<string>();
            foreach (string part in parts)
            {
                string? error = Syntax(part, out string code);
                if (error != null) return Refuse(code, error);
                if (!IsCovered(part, patterns))
                    return Refuse(ConnectorErrorCodes.PermissionDenied, "Index '" + part + "' is outside this resource's allowed index patterns (" + Describe(patterns) + "). Use es_indices to see the indexes you may query.");
                accepted.Add(part);
            }
            return new EsIndexCheck(true, String.Empty, String.Empty, String.Join(",", accepted));
        }

        /// <summary>
        /// Check an index expression and throw when it is refused.
        /// </summary>
        /// <param name="expression">Expression.</param>
        /// <param name="allowed">Allowed patterns.</param>
        /// <returns>Normalized expression.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> or <c>permission_denied</c>.</exception>
        public static string Require(string? expression, IList<string>? allowed)
        {
            EsIndexCheck check = Check(expression, allowed);
            if (!check.Allowed) throw new ConnectorException(check.Code, check.Reason);
            return check.Normalized;
        }

        /// <summary>
        /// Check only the syntax of an index pattern (used for listing, where results are filtered afterwards).
        /// </summary>
        /// <param name="expression">Expression.</param>
        /// <returns>Normalized expression.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for malformed patterns.</exception>
        public static string RequireSyntax(string expression)
        {
            List<string> parts = (expression ?? String.Empty).Split(',').Select(p => p.Trim()).ToList();
            foreach (string part in parts)
            {
                string? error = Syntax(part, out string code);
                if (error != null) throw new ConnectorException(code, error);
            }
            return String.Join(",", parts);
        }

        /// <summary>
        /// Whether a concrete index name may be read. Data stream backing indexes (<c>.ds-NAME-...</c>) are checked by
        /// the data stream part of their name.
        /// </summary>
        /// <param name="index">Index name.</param>
        /// <param name="allowed">Allowed patterns.</param>
        /// <returns>True when allowed.</returns>
        public static bool IsIndexAllowed(string? index, IList<string>? allowed)
        {
            if (String.IsNullOrEmpty(index)) return false;
            IList<string> patterns = allowed ?? new List<string>();
            if (IsCovered(index, patterns)) return true;
            if (index.StartsWith(".ds-", StringComparison.Ordinal)) return IsCovered(index.Substring(4), patterns);
            return false;
        }

        /// <summary>
        /// Glob match where <c>*</c> in the pattern matches any run of characters (including none) and every other
        /// character matches itself.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="pattern">Pattern.</param>
        /// <returns>True on a whole-string match.</returns>
        public static bool GlobMatch(string text, string pattern)
        {
            if (text == null || pattern == null) return false;
            int t = 0;
            int p = 0;
            int star = -1;
            int mark = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && pattern[p] != '*' && pattern[p] == text[t])
                {
                    t++;
                    p++;
                }
                else if (p < pattern.Length && pattern[p] == '*')
                {
                    star = p++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        #endregion

        #region Private-Methods

        private static string? Syntax(string part, out string code)
        {
            code = ConnectorErrorCodes.InvalidArgument;
            if (part.Length == 0) return "The index expression has an empty entry. Separate names with single commas.";
            if (part.Any(Char.IsWhiteSpace)) return "Index '" + part + "' contains whitespace.";
            if (part == "_all" || part.StartsWith('_'))
            {
                code = ConnectorErrorCodes.PermissionDenied;
                return "'" + part + "' is not allowed. Name indexes or patterns explicitly.";
            }
            if (part.StartsWith('-') || part.StartsWith('+')) return "Index exclusions and inclusions ('" + part + "') are not supported. List the indexes or patterns to read.";
            if (part.Contains(':'))
            {
                code = ConnectorErrorCodes.PermissionDenied;
                return "Cross-cluster index '" + part + "' is not allowed.";
            }
            if (part.IndexOfAny(new char[] { '<', '>', '{', '}' }) >= 0) return "Date math index names ('" + part + "') are not supported. Use a pattern such as logs-2026.10.*.";
            if (!_Part.IsMatch(part)) return "Index '" + part + "' has invalid characters. Index names are lowercase letters, digits, '.', '_', '-', and '*' wildcards.";
            return null;
        }

        private static bool IsCovered(string part, IList<string> patterns)
        {
            bool hidden = part.StartsWith('.');
            if (patterns.Count == 0) return !hidden;
            foreach (string raw in patterns)
            {
                string pattern = raw.Trim();
                if (pattern.Length == 0) continue;
                if (hidden && !pattern.StartsWith('.')) continue;
                if (GlobMatch(part, pattern)) return true;
            }
            return false;
        }

        private static string Describe(IList<string> patterns)
        {
            if (patterns.Count == 0) return "every index not starting with a dot";
            return String.Join(", ", patterns.Select(p => p.Trim()).Where(p => p.Length > 0));
        }

        private static EsIndexCheck Refuse(string code, string reason)
        {
            return new EsIndexCheck(false, code, reason, String.Empty);
        }

        #endregion
    }
}
