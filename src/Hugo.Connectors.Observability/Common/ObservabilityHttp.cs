namespace Hugo.Connectors.Observability
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Security;
    using System.Threading;

    /// <summary>
    /// Process-wide HTTP clients shared by the observability connectors: one that verifies TLS certificates and one that
    /// does not (for resources with <c>verifyTls = false</c>). Shared clients with pooled handlers avoid socket
    /// exhaustion; per-call timeouts come from cancellation tokens, so the clients' own timeouts are infinite.
    /// <see cref="HttpClient"/> is used directly instead of a wrapper library because it adds no dependency, honors
    /// cancellation tokens end to end, and lets responses be read as streams with a hard byte cap. Thread-safe.
    /// </summary>
    public static class ObservabilityHttp
    {
        #region Public-Members

        /// <summary>
        /// Client that verifies TLS certificates.
        /// </summary>
        public static HttpClient Verifying
        {
            get
            {
                return _Verifying.Value;
            }
        }

        /// <summary>
        /// Client that accepts any TLS certificate. Use only for resources whose operator disabled verification.
        /// </summary>
        public static HttpClient Insecure
        {
            get
            {
                return _Insecure.Value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<HttpClient> _Verifying = new Lazy<HttpClient>(() => Create(true), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<HttpClient> _Insecure = new Lazy<HttpClient>(() => Create(false), LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The client for a TLS verification choice.
        /// </summary>
        /// <param name="verifyTls">Whether certificates are verified.</param>
        /// <returns>Client.</returns>
        public static HttpClient For(bool verifyTls)
        {
            return verifyTls ? Verifying : Insecure;
        }

        #endregion

        #region Private-Methods

        private static HttpClient Create(bool verify)
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AllowAutoRedirect = false
            };
            if (!verify)
            {
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                };
            }
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hugo-Observability-Connector/1.0");
            return client;
        }

        #endregion
    }
}
