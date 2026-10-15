namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Sends authenticated requests to an observability backend, enforces the response byte cap, and maps failures to
    /// stable error codes. One instance serves one call; the underlying <see cref="HttpClient"/> is shared. Thread-safe
    /// for concurrent requests.
    /// </summary>
    public class ObservabilityTransport
    {
        #region Public-Members

        /// <summary>
        /// Endpoint settings.
        /// </summary>
        public ObservabilityEndpoint Endpoint { get; }

        /// <summary>
        /// Provider display name used in error messages (for example <c>Prometheus</c>).
        /// </summary>
        public string Provider { get; }

        /// <summary>
        /// Maximum characters of an upstream error message included in an error. Minimum 32, maximum 4096, default 500.
        /// </summary>
        public int MaxErrorMessageChars
        {
            get
            {
                return _MaxErrorMessageChars;
            }
            set
            {
                _MaxErrorMessageChars = Math.Clamp(value, 32, 4096);
            }
        }

        #endregion

        #region Private-Members

        private readonly SecretAccessor _Secrets;
        private readonly Dictionary<string, string> _ExtraHeaders;
        private readonly IConnectorLogger _Logger;
        private readonly HttpClient _Client;
        private int _MaxErrorMessageChars = 500;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <param name="provider">Provider display name.</param>
        /// <param name="secrets">Secrets of the call.</param>
        /// <param name="extraHeaders">Headers added to every request after the default headers (for example the Loki tenant header). Null means none.</param>
        /// <param name="logger">Logger. Null uses the null logger.</param>
        /// <param name="client">HTTP client. Null uses the shared client for the endpoint's TLS setting.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint, provider, or secrets is null.</exception>
        public ObservabilityTransport(ObservabilityEndpoint endpoint, string provider, SecretAccessor secrets, IDictionary<string, string>? extraHeaders = null, IConnectorLogger? logger = null, HttpClient? client = null)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            if (String.IsNullOrWhiteSpace(provider)) throw new ArgumentNullException(nameof(provider));
            Provider = provider;
            _Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _ExtraHeaders = extraHeaders == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(extraHeaders, StringComparer.OrdinalIgnoreCase);
            _Logger = logger ?? NullConnectorLogger.Instance;
            _Client = client ?? ObservabilityHttp.For(endpoint.VerifyTls);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send a GET request and return the body of a successful response.
        /// </summary>
        /// <param name="path">Path relative to the base URL.</param>
        /// <param name="query">Query parameters; entries with null or empty values are skipped. Null means none.</param>
        /// <param name="token">Cancellation token carrying the call timeout.</param>
        /// <returns>Response body.</returns>
        /// <exception cref="ObservabilityUpstreamException">Thrown for a non-success status.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>connection_failed</c> or <c>too_large</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public Task<string> GetAsync(string path, IList<KeyValuePair<string, string?>>? query, CancellationToken token)
        {
            return SendAsync(HttpMethod.Get, path, query, null, token);
        }

        /// <summary>
        /// Send a POST request with a JSON body and return the body of a successful response.
        /// </summary>
        /// <param name="path">Path relative to the base URL.</param>
        /// <param name="query">Query parameters, or null.</param>
        /// <param name="json">JSON body.</param>
        /// <param name="token">Cancellation token carrying the call timeout.</param>
        /// <returns>Response body.</returns>
        /// <exception cref="ObservabilityUpstreamException">Thrown for a non-success status.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>connection_failed</c> or <c>too_large</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public Task<string> PostJsonAsync(string path, IList<KeyValuePair<string, string?>>? query, string json, CancellationToken token)
        {
            return SendAsync(HttpMethod.Post, path, query, json ?? "{}", token);
        }

        /// <summary>
        /// Build the absolute URL of a path and query.
        /// </summary>
        /// <param name="path">Path relative to the base URL.</param>
        /// <param name="query">Query parameters, or null.</param>
        /// <returns>URL.</returns>
        public string BuildUrl(string path, IList<KeyValuePair<string, string?>>? query)
        {
            StringBuilder sb = new StringBuilder(Endpoint.BaseUrl);
            sb.Append('/').Append((path ?? String.Empty).TrimStart('/'));
            if (query != null)
            {
                bool first = true;
                foreach (KeyValuePair<string, string?> pair in query.Where(p => !String.IsNullOrEmpty(p.Value)))
                {
                    sb.Append(first ? '?' : '&');
                    sb.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value!));
                    first = false;
                }
            }
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private async Task<string> SendAsync(HttpMethod method, string path, IList<KeyValuePair<string, string?>>? query, string? json, CancellationToken token)
        {
            string url = BuildUrl(path, query);
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                Stopwatch sw = Stopwatch.StartNew();
                using (HttpRequestMessage request = new HttpRequestMessage(method, url))
                {
                    foreach (KeyValuePair<string, string> header in Endpoint.DefaultHeaders) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    foreach (KeyValuePair<string, string> header in _ExtraHeaders)
                    {
                        request.Headers.Remove(header.Key);
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                    request.Headers.Accept.ParseAdd("application/json");
                    if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    string? oauthKey = await ObservabilityAuth.ApplyAsync(request, Endpoint, _Secrets, _Client, token).ConfigureAwait(false);

                    HttpResponseMessage response;
                    try
                    {
                        response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                    {
                        throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, Provider + " could not be reached at " + Endpoint.BaseUrl + ": " + e.Message, e);
                    }

                    using (response)
                    {
                        int status = (int)response.StatusCode;
                        if (status == 401 && oauthKey != null && attempt == 0)
                        {
                            OAuth2TokenCache.Invalidate(oauthKey);
                            _Logger.Debug(Provider + " rejected the cached OAuth2 token; requesting a new one.");
                            continue;
                        }

                        string body = await ReadCappedAsync(response, token).ConfigureAwait(false);
                        _Logger.Debug(Provider + " " + method.Method + " " + path + " returned " + status + " in " + sw.ElapsedMilliseconds + " ms");
                        if (response.IsSuccessStatusCode) return body;
                        throw MapFailure(response, status, body);
                    }
                }
            }
        }

        private async Task<string> ReadCappedAsync(HttpResponseMessage response, CancellationToken token)
        {
            int cap = Endpoint.MaxResponseBytes;
            long? declared = response.Content.Headers.ContentLength;
            if (declared.HasValue && declared.Value > cap) throw TooLarge(declared.Value);
            using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (buffer.Length + read > cap) throw TooLarge(-1);
                    buffer.Write(chunk, 0, read);
                }
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }

        private ConnectorException TooLarge(long declared)
        {
            string size = declared > 0 ? declared.ToString(CultureInfo.InvariantCulture) + " bytes" : "more than " + Endpoint.MaxResponseBytes.ToString(CultureInfo.InvariantCulture) + " bytes";
            return new ConnectorException(ConnectorErrorCodes.TooLarge, Provider + " returned " + size + ", over the resource's maxResponseBytes limit. Narrow the query, shorten the range, or lower the limit.");
        }

        private ObservabilityUpstreamException MapFailure(HttpResponseMessage response, int status, string body)
        {
            string detail = Shorten(ObservabilityJson.ErrorMessage(body));
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                return new ObservabilityUpstreamException(ConnectorErrorCodes.AuthFailed, Provider + " rejected the credentials (HTTP " + status + "). Check the resource's authType and bound secret." + Suffix(detail), status, null);

            if (status == 429)
            {
                double? retryAfter = ReadRetryAfter(response);
                string wait = retryAfter.HasValue ? " Retry after " + retryAfter.Value.ToString("0", CultureInfo.InvariantCulture) + " seconds." : " Wait before retrying.";
                return new ObservabilityUpstreamException(ConnectorErrorCodes.RateLimited, Provider + " is throttling requests (HTTP 429)." + wait, status, retryAfter);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new ObservabilityUpstreamException(ConnectorErrorCodes.NotFound, Provider + " returned 404 Not Found." + Suffix(detail), status, null);

            if (response.StatusCode == HttpStatusCode.BadRequest || status == 422)
                return new ObservabilityUpstreamException(ConnectorErrorCodes.InvalidArgument, Provider + " rejected the request (HTTP " + status + ")." + Suffix(detail), status, null);

            if (status == 503 || status == 504)
                return new ObservabilityUpstreamException(ConnectorErrorCodes.UpstreamError, Provider + " is unavailable or timed out (HTTP " + status + "). Narrow the query or try again later." + Suffix(detail), status, null);

            return new ObservabilityUpstreamException(ConnectorErrorCodes.UpstreamError, Provider + " returned HTTP " + status + "." + Suffix(detail), status, null);
        }

        private static double? ReadRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter == null) return null;
            if (response.Headers.RetryAfter.Delta.HasValue) return Math.Ceiling(Math.Max(0, response.Headers.RetryAfter.Delta.Value.TotalSeconds));
            if (response.Headers.RetryAfter.Date.HasValue) return Math.Ceiling(Math.Max(0, (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds));
            return null;
        }

        private string Shorten(string? text)
        {
            string value = (text ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length > _MaxErrorMessageChars) value = value.Substring(0, _MaxErrorMessageChars) + "...";
            return value;
        }

        private static string Suffix(string detail)
        {
            return String.IsNullOrEmpty(detail) ? String.Empty : " Detail: " + detail;
        }

        #endregion
    }
}
