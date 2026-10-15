namespace Hugo.Connectors.Http
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Net.Security;
    using System.Threading;

    /// <summary>
    /// Process-wide HTTP clients shared by every <c>http</c> resource: one that verifies TLS certificates and one that
    /// does not (for resources with <c>verifyTls = false</c>). Both disable automatic redirects and cookies, because
    /// <see cref="HttpSender"/> follows redirects itself so that every hop is checked against the resource's policy.
    /// Per-call timeouts come from cancellation tokens, so the clients' own timeout is infinite.
    /// <para>
    /// The connector uses <see cref="HttpClient"/> rather than RestWrapper: it adds no dependency (like the logging
    /// connectors), allows manual redirect handling so auth headers never leave the base origin, exposes the TLS
    /// certificate to <c>http_health_check</c> through a per-call certificate callback, and supports a streaming read
    /// with a hard byte cap.
    /// </para>
    /// Thread-safe.
    /// </summary>
    public static class HttpConnectorClients
    {
        #region Public-Members

        /// <summary>
        /// Client that verifies certificates.
        /// </summary>
        public static HttpClient Verified
        {
            get
            {
                return _Verified.Value;
            }
        }

        /// <summary>
        /// Client that accepts any certificate.
        /// </summary>
        public static HttpClient Unverified
        {
            get
            {
                return _Unverified.Value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<HttpClient> _Verified = new Lazy<HttpClient>(() => Create(true), LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<HttpClient> _Unverified = new Lazy<HttpClient>(() => Create(false), LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The client for a verification mode.
        /// </summary>
        /// <param name="verifyTls">Whether certificates are verified.</param>
        /// <returns>Client.</returns>
        public static HttpClient For(bool verifyTls)
        {
            return verifyTls ? Verified : Unverified;
        }

        #endregion

        #region Private-Methods

        private static HttpClient Create(bool verifyTls)
        {
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            if (!verifyTls)
            {
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                };
            }
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hugo-Http-Connector/1.0");
            return client;
        }

        #endregion
    }
}
