namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Sends authenticated GET requests to a log service and maps failures to stable error codes. One instance serves
    /// one call; the underlying <see cref="HttpClient"/> is shared. Thread-safe for concurrent requests.
    /// </summary>
    public class LoggingHttpTransport
    {
        #region Public-Members

        /// <summary>
        /// Base URL without a trailing slash (for example <c>https://papertrailapp.com/api/v1</c>).
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Provider display name used in error messages (for example <c>Papertrail</c>).
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

        private readonly HttpClient _Client;
        private readonly Dictionary<string, string> _Headers;
        private readonly IConnectorLogger _Logger;
        private int _MaxErrorMessageChars = 500;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseUrl">Absolute base URL.</param>
        /// <param name="provider">Provider display name.</param>
        /// <param name="headers">Headers sent with every request (for example the authentication header).</param>
        /// <param name="logger">Logger. Null uses the null logger.</param>
        /// <param name="client">HTTP client. Null uses <see cref="LoggingHttp.Client"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when baseUrl, provider, or headers is null.</exception>
        /// <exception cref="ArgumentException">Thrown when baseUrl is not an absolute http or https URL.</exception>
        public LoggingHttpTransport(string baseUrl, string provider, IDictionary<string, string> headers, IConnectorLogger? logger = null, HttpClient? client = null)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            if (String.IsNullOrWhiteSpace(provider)) throw new ArgumentNullException(nameof(provider));
            ArgumentNullException.ThrowIfNull(headers);
            if (!IsHttpUrl(baseUrl)) throw new ArgumentException("Base URL '" + baseUrl + "' is not an absolute http or https URL.", nameof(baseUrl));

            BaseUrl = baseUrl.TrimEnd('/');
            Provider = provider;
            _Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            _Logger = logger ?? NullConnectorLogger.Instance;
            _Client = client ?? LoggingHttp.Client;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a value is an absolute http or https URL.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsHttpUrl(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Send a GET request and return the body of a successful response.
        /// </summary>
        /// <param name="path">Path relative to the base URL (for example <c>events/search.json</c>).</param>
        /// <param name="query">Query parameters. Entries with null or empty values are skipped. Null means none.</param>
        /// <param name="token">Cancellation token. Callers apply the call timeout through this token.</param>
        /// <returns>Response body.</returns>
        /// <exception cref="LoggingUpstreamException">Thrown for a non-success status.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>connection_failed</c> when the service is unreachable.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<string> GetAsync(string path, IList<KeyValuePair<string, string?>>? query, CancellationToken token)
        {
            string url = BuildUrl(path, query);
            Stopwatch sw = Stopwatch.StartNew();
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                foreach (KeyValuePair<string, string> header in _Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                request.Headers.Accept.ParseAdd("application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new ConnectorException("connection_failed", Provider + " could not be reached at " + BaseUrl + ": " + e.Message, e);
                }

                using (response)
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    _Logger.Debug(Provider + " GET " + path + " returned " + status + " in " + sw.ElapsedMilliseconds + " ms");
                    if (response.IsSuccessStatusCode) return body;
                    throw MapFailure(response, status, body);
                }
            }
        }

        #endregion

        #region Private-Methods

        private string BuildUrl(string path, IList<KeyValuePair<string, string?>>? query)
        {
            StringBuilder sb = new StringBuilder(BaseUrl);
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

        private LoggingUpstreamException MapFailure(HttpResponseMessage response, int status, string body)
        {
            UpstreamErrorBody? error = LoggingJson.TryReadError(body);
            string detail = error != null && !String.IsNullOrWhiteSpace(error.Message) ? error.Message : body;
            detail = Shorten(detail);
            if (error != null && !String.IsNullOrWhiteSpace(error.Target)) detail = detail + " (field " + error.Target + ")";

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new LoggingUpstreamException(
                    "auth_failed",
                    Provider + " rejected the API token (HTTP " + status + "). Check the apiToken secret on this resource and its permissions." + Suffix(detail),
                    status,
                    null);
            }

            if (status == 429)
            {
                int? retryAfter = ReadRetryAfter(response);
                string wait = retryAfter.HasValue ? " Retry after " + retryAfter.Value + " seconds." : " Wait before retrying.";
                return new LoggingUpstreamException(
                    "rate_limited",
                    Provider + " rate limit exceeded (HTTP 429)." + wait + " Reduce the number of calls, or widen the window and lower the limit.",
                    status,
                    retryAfter);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LoggingUpstreamException("not_found", Provider + " returned 404 Not Found." + Suffix(detail), status, null);

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new LoggingUpstreamException("invalid_argument", Provider + " rejected the request (HTTP 400)." + Suffix(detail), status, null);

            return new LoggingUpstreamException("upstream_error", Provider + " returned HTTP " + status + "." + Suffix(detail), status, null);
        }

        private static int? ReadRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) return (int)Math.Ceiling(Math.Max(0, response.Headers.RetryAfter.Delta.Value.TotalSeconds));
                if (response.Headers.RetryAfter.Date.HasValue)
                    return (int)Math.Ceiling(Math.Max(0, (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds));
            }

            if (response.Headers.TryGetValues("X-Rate-Limit-Reset", out IEnumerable<string>? values))
            {
                string? first = values.FirstOrDefault();
                if (first != null && Double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds >= 0)
                    return (int)Math.Ceiling(seconds);
            }

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
