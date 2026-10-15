namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The resource's <c>includeTools</c> and <c>excludeTools</c> lists. Entries are exact remote tool names or patterns
    /// where <c>*</c> matches any run of characters (for example <c>get_*</c>). Matching is case-sensitive, like MCP tool
    /// names. An empty include list allows every tool; an exclude match always wins. Immutable, thread-safe.
    /// </summary>
    public class McpToolFilter
    {
        #region Public-Members

        /// <summary>
        /// Include patterns. Never null.
        /// </summary>
        public IReadOnlyList<string> Include { get; }

        /// <summary>
        /// Exclude patterns. Never null.
        /// </summary>
        public IReadOnlyList<string> Exclude { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="include">Include patterns. Null or empty allows every tool.</param>
        /// <param name="exclude">Exclude patterns. Null is treated as empty.</param>
        public McpToolFilter(IEnumerable<string>? include, IEnumerable<string>? exclude)
        {
            Include = Clean(include);
            Exclude = Clean(exclude);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a remote tool may be listed and called on this resource.
        /// </summary>
        /// <param name="toolName">Remote tool name.</param>
        /// <returns>True when allowed.</returns>
        public bool IsAllowed(string? toolName)
        {
            if (String.IsNullOrEmpty(toolName)) return false;
            if (Exclude.Any(p => Matches(p, toolName))) return false;
            if (!Include.Any()) return true;
            return Include.Any(p => Matches(p, toolName));
        }

        /// <summary>
        /// Match a name against one pattern where <c>*</c> matches any run of characters.
        /// </summary>
        /// <param name="pattern">Pattern.</param>
        /// <param name="name">Name.</param>
        /// <returns>True when the whole name matches.</returns>
        public static bool Matches(string pattern, string name)
        {
            if (pattern == null || name == null) return false;
            if (!pattern.Contains('*', StringComparison.Ordinal)) return String.Equals(pattern, name, StringComparison.Ordinal);

            string[] parts = pattern.Split('*');
            int position = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i == 0)
                {
                    if (!name.StartsWith(part, StringComparison.Ordinal)) return false;
                    position = part.Length;
                    continue;
                }
                if (i == parts.Length - 1)
                {
                    if (part.Length == 0) return true;
                    return name.Length - part.Length >= position && name.EndsWith(part, StringComparison.Ordinal);
                }
                if (part.Length == 0) continue;
                int found = name.IndexOf(part, position, StringComparison.Ordinal);
                if (found < 0) return false;
                position = found + part.Length;
            }
            return position == name.Length;
        }

        #endregion

        #region Private-Methods

        private static List<string> Clean(IEnumerable<string>? values)
        {
            if (values == null) return new List<string>();
            return values
                .SelectMany(v => (v ?? String.Empty).Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        #endregion
    }
}
