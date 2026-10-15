namespace Hugo.Connectors.Docker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Redacts environment variable values (<c>NAME=value</c> becomes <c>NAME=&lt;redacted&gt;</c>) so variable names
    /// stay visible while credentials passed through the environment do not reach the model. Thread-safe.
    /// </summary>
    public static class DockerEnvironmentRedactor
    {
        #region Public-Members

        /// <summary>
        /// Replacement for a redacted value.
        /// </summary>
        public static string Marker { get; } = "<redacted>";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Redact a list of <c>NAME=value</c> entries.
        /// </summary>
        /// <param name="environment">Entries.</param>
        /// <returns>Redacted entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when environment is null.</exception>
        public static IList<string> Redact(IList<string> environment)
        {
            ArgumentNullException.ThrowIfNull(environment);
            return environment.Select(RedactEntry).ToList();
        }

        /// <summary>
        /// Redact one entry.
        /// </summary>
        /// <param name="entry">Entry such as <c>DB_PASSWORD=hunter2</c>.</param>
        /// <returns>Entry such as <c>DB_PASSWORD=&lt;redacted&gt;</c>; entries without a value are unchanged.</returns>
        public static string RedactEntry(string entry)
        {
            if (String.IsNullOrEmpty(entry)) return entry ?? String.Empty;
            int eq = entry.IndexOf('=');
            if (eq < 0) return entry;
            if (eq == entry.Length - 1) return entry;
            return entry.Substring(0, eq + 1) + Marker;
        }

        #endregion
    }
}
