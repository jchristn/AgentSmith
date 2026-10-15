namespace Hugo.Connectors.Observability
{
    using System;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Pre-flight checks for LogQL queries. Thread-safe.
    /// </summary>
    public static class LokiQueryGuard
    {
        #region Private-Members

        private static readonly Regex _Selector = new Regex("\\{\\s*[A-Za-z_][A-Za-z0-9_]*\\s*(=~|!~|!=|=)\\s*(\"|`)", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a LogQL query contains a stream selector with at least one label matcher, such as
        /// <c>{app="api"}</c>.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <returns>True when a selector is present.</returns>
        public static bool HasStreamSelector(string? query)
        {
            return !String.IsNullOrWhiteSpace(query) && _Selector.IsMatch(query);
        }

        /// <summary>
        /// Throw when a query has no stream selector.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when no selector is present.</exception>
        public static void RequireStreamSelector(string? query)
        {
            if (!HasStreamSelector(query))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "LogQL queries need a stream selector with at least one label matcher, for example {app=\"api\"} |= \"error\" or sum(rate({app=\"api\"}[5m])). Use loki_labels and loki_label_values to find labels.");
        }

        #endregion
    }
}
