namespace Hugo.Cli.Arguments
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The options and positional arguments of one command line. Not thread-safe.
    /// </summary>
    public class ParsedArguments
    {
        #region Public-Members

        /// <summary>
        /// Option values by name (without dashes). Names compare case-sensitively.
        /// </summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Flags that were present.
        /// </summary>
        public HashSet<string> Flags { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Positional arguments, in order.
        /// </summary>
        public List<string> Positionals { get; } = new List<string>();

        /// <summary>
        /// Whether <c>--help</c> or <c>-h</c> was present.
        /// </summary>
        public bool HelpRequested
        {
            get
            {
                return Flags.Contains("help");
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The value of an option, or null when it was not given or was empty.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <returns>Value or null.</returns>
        public string? Get(string name)
        {
            if (Values.TryGetValue(name, out string? value) && !String.IsNullOrEmpty(value)) return value;
            return null;
        }

        /// <summary>
        /// Whether a flag was present.
        /// </summary>
        /// <param name="name">Flag name without dashes.</param>
        /// <returns>True when present.</returns>
        public bool Has(string name)
        {
            return Flags.Contains(name);
        }

        #endregion
    }
}
