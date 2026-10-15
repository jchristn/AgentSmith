namespace Hugo.Connectors.Redis.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Resource-level adjustments to the Redis command classifier. Not thread-safe for mutation.
    /// </summary>
    public class RedisClassifierOptions
    {
        #region Public-Members

        /// <summary>
        /// Commands (upper case, optionally with a subcommand such as <c>CONFIG GET</c>) that are rejected outright.
        /// Default contains <c>KEYS</c>, which blocks the server while it walks every key. <see cref="From"/> builds the
        /// effective set: the resource's <c>deniedCommands</c> plus <c>KEYS</c> unless <c>allowKeysCommand</c> is on.
        /// </summary>
        public HashSet<string> Denied { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KEYS" };

        /// <summary>
        /// Commands not in the built-in table (for example module commands such as <c>JSON.GET</c>) that are classified
        /// as Read. They never change a built-in classification. Default empty.
        /// </summary>
        public HashSet<string> ExtraReadOnly { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build from connection options.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <returns>Classifier options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public static RedisClassifierOptions From(RedisConnectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            RedisClassifierOptions o = new RedisClassifierOptions();
            o.Denied = new HashSet<string>(options.DeniedCommands.Select(Normalize).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            if (!options.AllowKeysCommand) o.Denied.Add("KEYS");
            o.ExtraReadOnly = new HashSet<string>(options.ReadOnlyCommands.Select(Normalize).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            return o;
        }

        #endregion

        #region Private-Methods

        private static string Normalize(string value)
        {
            return String.Join(" ", (value ?? String.Empty).Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        #endregion
    }
}
