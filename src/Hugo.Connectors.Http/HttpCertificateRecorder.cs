namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Net.Security;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;

    /// <summary>
    /// Records the certificate each TLS endpoint presents during one <c>http_health_check</c> call. It builds a
    /// dedicated client whose handler is used for that call only, so every endpoint opens a new connection and the
    /// certificate callback always runs. Thread-safe.
    /// </summary>
    public class HttpCertificateRecorder
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, HttpCertificateInfo> _Seen = new ConcurrentDictionary<string, HttpCertificateInfo>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a client for one call. Dispose it when the call ends.
        /// </summary>
        /// <param name="verifyTls">Whether invalid certificates fail the handshake.</param>
        /// <returns>Client.</returns>
        public HttpClient CreateClient(bool verifyTls)
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => Record(request, certificate, errors, verifyTls)
            };
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hugo-Http-Connector/1.0");
            return client;
        }

        /// <summary>
        /// The certificate recorded for a URL's host and port, or null.
        /// </summary>
        /// <param name="url">URL.</param>
        /// <returns>Certificate information or null.</returns>
        public HttpCertificateInfo? For(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            return _Seen.TryGetValue(Endpoint(url), out HttpCertificateInfo? info) ? info : null;
        }

        #endregion

        #region Private-Methods

        private bool Record(HttpRequestMessage request, X509Certificate2? certificate, SslPolicyErrors errors, bool verifyTls)
        {
            if (certificate != null && request.RequestUri != null)
            {
                DateTime notAfter = certificate.NotAfter.ToUniversalTime();
                HttpCertificateInfo info = new HttpCertificateInfo
                {
                    Endpoint = Endpoint(request.RequestUri),
                    Subject = certificate.Subject,
                    Issuer = certificate.Issuer,
                    NotBefore = certificate.NotBefore.ToUniversalTime(),
                    NotAfter = notAfter,
                    DaysRemaining = (int)Math.Floor((notAfter - DateTime.UtcNow).TotalDays),
                    Sha256Thumbprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                    Valid = errors == SslPolicyErrors.None,
                    Errors = errors.ToString()
                };
                _Seen[info.Endpoint] = info;
            }
            return errors == SslPolicyErrors.None || !verifyTls;
        }

        private static string Endpoint(Uri url)
        {
            return url.Host + ":" + url.Port.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
