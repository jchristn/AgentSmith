namespace Hugo.Core.Safety
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Binding command deny patterns (plan section 8.3, ssh_exec step 1; follow-up on P3.13). A binding lists .NET
    /// regular expressions; the pipeline matches them, case-insensitively, against the primary argument of a per-call
    /// classified tool (see <see cref="Hugo.Core.Connectors.ToolArgumentValidator.PrimaryArgument"/>) before the
    /// classifier runs, and denies a match with code binding_deny_pattern. A pattern that times out counts as a match
    /// (fail closed). Thread-safe.
    /// </summary>
    public static class DenyPatternMatcher
    {
        #region Public-Members

        /// <summary>
        /// Match timeout per pattern. Default 100 ms.
        /// </summary>
        public static TimeSpan MatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Maximum patterns per binding. Default 64.
        /// </summary>
        public static int MaxPatterns { get; set; } = 64;

        /// <summary>
        /// Maximum length of one pattern. Default 1024.
        /// </summary>
        public static int MaxPatternLength { get; set; } = 1024;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find the first pattern that matches a value.
        /// </summary>
        /// <param name="patterns">Patterns.</param>
        /// <param name="value">Value (for example a shell command or SQL statement).</param>
        /// <returns>The matching pattern, or null.</returns>
        public static string? FirstMatch(IEnumerable<string>? patterns, string? value)
        {
            if (patterns == null || value == null) return null;
            foreach (string pattern in patterns)
            {
                if (String.IsNullOrWhiteSpace(pattern)) continue;
                try
                {
                    if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout)) return pattern;
                }
                catch (RegexMatchTimeoutException)
                {
                    return pattern;
                }
                catch (ArgumentException)
                {
                    return pattern;
                }
            }
            return null;
        }

        /// <summary>
        /// Validate patterns before a binding is saved.
        /// </summary>
        /// <param name="patterns">Patterns.</param>
        /// <returns>Messages, empty when valid.</returns>
        public static List<string> Validate(IEnumerable<string>? patterns)
        {
            List<string> errors = new List<string>();
            if (patterns == null) return errors;
            int count = 0;
            foreach (string pattern in patterns)
            {
                count++;
                if (String.IsNullOrWhiteSpace(pattern))
                {
                    errors.Add("Deny pattern " + count + " is empty.");
                    continue;
                }
                if (pattern.Length > MaxPatternLength)
                {
                    errors.Add("Deny pattern " + count + " is longer than " + MaxPatternLength + " characters.");
                    continue;
                }
                try
                {
                    Regex.IsMatch(String.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
                }
                catch (ArgumentException e)
                {
                    errors.Add("Deny pattern " + count + " is not a valid regular expression: " + e.Message);
                }
                catch (RegexMatchTimeoutException)
                {
                    errors.Add("Deny pattern " + count + " is too expensive to evaluate.");
                }
            }
            if (count > MaxPatterns) errors.Add("At most " + MaxPatterns + " deny patterns are allowed.");
            return errors;
        }

        #endregion
    }
}
