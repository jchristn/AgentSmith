namespace Hugo.Core.Security
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// One secret scrubber pattern: a redaction kind, a regular expression, and an optional substitution that keeps a
    /// non-secret prefix (for example the key name in password=...).
    /// </summary>
    internal class ScrubPattern
    {
        #region Public-Members

        /// <summary>
        /// Redaction kind, used in [redacted:kind] and as the metric label.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Pattern.
        /// </summary>
        public Regex Regex { get; }

        /// <summary>
        /// Substitution kept before the redaction marker (for example $1$2), or null to replace the whole match.
        /// </summary>
        public string? Keep { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="regex">Pattern.</param>
        /// <param name="keep">Kept substitution, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when kind or regex is null.</exception>
        public ScrubPattern(string kind, Regex regex, string? keep)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Regex = regex ?? throw new ArgumentNullException(nameof(regex));
            Keep = keep;
        }

        #endregion
    }
}
