namespace Hugo.Connectors.Logging
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading;

    /// <summary>
    /// Process-wide HTTP client shared by the logging connectors. A single <see cref="HttpClient"/> with a pooled
    /// handler avoids socket exhaustion; per-call timeouts come from cancellation tokens, so the client's own timeout
    /// is infinite. Thread-safe.
    /// </summary>
    public static class LoggingHttp
    {
        #region Public-Members

        /// <summary>
        /// The shared client. Created on first use.
        /// </summary>
        public static HttpClient Client
        {
            get
            {
                return _Client.Value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<HttpClient> _Client = new Lazy<HttpClient>(Create, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Private-Methods

        private static HttpClient Create()
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hugo-Logging-Connector/1.0");
            return client;
        }

        #endregion
    }
}
