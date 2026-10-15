namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Locations of the observability testbed (<c>docker/testbed/observability/compose.yaml</c>). Ports can be
    /// overridden with <c>HUGO_TESTBED_PROMETHEUS_PORT</c>, <c>HUGO_TESTBED_LOKI_PORT</c>, and
    /// <c>HUGO_TESTBED_OPENSEARCH_PORT</c>.
    /// </summary>
    public static class ObservabilityTestbed
    {
        #region Public-Members

        /// <summary>
        /// Whether testbed suites run (<c>HUGO_TESTBED=1</c>).
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED") == "1";
            }
        }

        /// <summary>
        /// Skip reason when the testbed is disabled.
        /// </summary>
        public static string SkipReason
        {
            get
            {
                return "Observability testbed not enabled. Start docker/testbed/observability/compose.yaml and set HUGO_TESTBED=1.";
            }
        }

        /// <summary>
        /// Prometheus base URL.
        /// </summary>
        public static string PrometheusUrl
        {
            get
            {
                return "http://127.0.0.1:" + Port("HUGO_TESTBED_PROMETHEUS_PORT", 28090);
            }
        }

        /// <summary>
        /// Loki base URL.
        /// </summary>
        public static string LokiUrl
        {
            get
            {
                return "http://127.0.0.1:" + Port("HUGO_TESTBED_LOKI_PORT", 28100);
            }
        }

        /// <summary>
        /// OpenSearch base URL.
        /// </summary>
        public static string OpenSearchUrl
        {
            get
            {
                return "http://127.0.0.1:" + Port("HUGO_TESTBED_OPENSEARCH_PORT", 28992);
            }
        }

        /// <summary>
        /// Loki tenant used by the seed.
        /// </summary>
        public static string LokiTenant { get; } = "hugo-testbed";

        #endregion

        #region Private-Methods

        private static string Port(string variable, int fallback)
        {
            string? v = Environment.GetEnvironmentVariable(variable);
            int port = v != null && Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
            return port.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
