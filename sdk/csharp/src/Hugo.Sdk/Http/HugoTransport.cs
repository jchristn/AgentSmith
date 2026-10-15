namespace Hugo.Sdk.Http
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// HTTP transport shared by every SDK module: JSON serialization (camelCase, enums as strings, nulls omitted,
    /// unknown fields ignored), authentication headers, signed requests, automatic session refresh, error mapping,
    /// and server-sent event streaming. Thread-safe for concurrent requests; change credentials only between requests.
    /// </summary>
    public class HugoTransport : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL without a trailing slash. Default http://127.0.0.1:8800.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string BaseUrl
        {
            get
            {
                return _BaseUrl;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(BaseUrl));
                _BaseUrl = value.TrimEnd('/');
            }
        }

        /// <summary>
        /// Authentication mode. Default None.
        /// </summary>
        public AuthModeEnum AuthMode { get; set; } = AuthModeEnum.None;

        /// <summary>
        /// Bearer session token, used when AuthMode is Token.
        /// </summary>
        public string? Token { get; set; } = null;

        /// <summary>
        /// Expiry of the session token, UTC, when known. Used by automatic refresh.
        /// </summary>
        public DateTime? TokenExpiresUtc { get; set; } = null;

        /// <summary>
        /// Credential access key, used when AuthMode is AccessKey or SignedRequest.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// Credential secret key, used when AuthMode is AccessKey or SignedRequest.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// System administrator API key, used when AuthMode is AdminApiKey.
        /// </summary>
        public string? AdminApiKey { get; set; } = null;

        /// <summary>
        /// Tenant identifier used for tenant-scoped paths, the x-tenant-id header, and request signatures.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Tenant that system administrators address on the flat Hugo routes (/v1.0/api/resources, /agent-profiles,
        /// and so on, where the tenant otherwise comes from the credentials). When set, those requests carry
        /// ?tenantId=. Null (the default) uses the caller's own tenant.
        /// </summary>
        public string? TenantOverride { get; set; } = null;

        /// <summary>
        /// Whether a token that expires within RefreshSkewSeconds is refreshed (POST /v1.0/token/refresh) before a
        /// request is sent. Applies only when AuthMode is Token and TokenExpiresUtc is known. Default true.
        /// </summary>
        public bool AutoRefresh { get; set; } = true;

        /// <summary>
        /// Seconds before expiry at which automatic refresh happens. Minimum 0, maximum 3600, default 60.
        /// </summary>
        public int RefreshSkewSeconds
        {
            get
            {
                return _RefreshSkewSeconds;
            }
            set
            {
                _RefreshSkewSeconds = Math.Clamp(value, 0, 3600);
            }
        }

        /// <summary>
        /// How many times a request answered with 429 (for example RateLimited on /v1.0/token, whose default limit is
        /// Auth.RateLimitPerMinute = 10 per minute per source IP and per identity) is retried after waiting. Minimum 0,
        /// maximum 10, default 0 (no retry: the 429 surfaces as a HugoException). Streaming requests are not retried.
        /// </summary>
        public int RateLimitRetries
        {
            get
            {
                return _RateLimitRetries;
            }
            set
            {
                _RateLimitRetries = Math.Clamp(value, 0, 10);
            }
        }

        /// <summary>
        /// Longest wait before a 429 retry, in seconds. The wait is the response's Retry-After seconds when present,
        /// otherwise an exponential backoff (2, 4, 8, ... seconds), capped at this value. Minimum 1, maximum 600,
        /// default 90.
        /// </summary>
        public int RateLimitMaxWaitSeconds
        {
            get
            {
                return _RateLimitMaxWaitSeconds;
            }
            set
            {
                _RateLimitMaxWaitSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// User-Agent header value. Default Hugo.Sdk/0.1.0-alpha.
        /// </summary>
        public string UserAgent { get; set; } = "Hugo.Sdk/" + HugoClient.SdkVersion;

        /// <summary>
        /// JSON serializer options used for requests and responses.
        /// </summary>
        public static JsonSerializerOptions JsonOptions
        {
            get
            {
                return _JsonOptions;
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = BuildOptions();
        private static readonly string _RefreshPath = "/v1.0/token/refresh";
        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private readonly SemaphoreSlim _RefreshLock = new SemaphoreSlim(1, 1);
        private string _BaseUrl = "http://127.0.0.1:8800";
        private int _RefreshSkewSeconds = 60;
        private int _RateLimitRetries = 0;
        private int _RateLimitMaxWaitSeconds = 90;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseUrl">Base URL, for example http://127.0.0.1:8800.</param>
        /// <param name="httpClient">Optional HttpClient; when null the transport owns one with a 10 minute timeout.</param>
        public HugoTransport(string baseUrl, HttpClient? httpClient = null)
        {
            BaseUrl = baseUrl;
            if (httpClient == null)
            {
                _Http = new HttpClient();
                _Http.Timeout = TimeSpan.FromMinutes(10);
                _OwnsHttp = true;
            }
            else
            {
                _Http = httpClient;
                _OwnsHttp = false;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send an authenticated request and deserialize the JSON response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path beginning with a slash, without query.</param>
        /// <param name="body">Optional body serialized as JSON.</param>
        /// <param name="query">Optional query parameters; null and empty values are skipped.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deserialized response, or default for empty bodies.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, IDictionary<string, string?>? query = null, CancellationToken token = default)
        {
            string text = await SendTextAsync(method, path, body, query, null, true, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(text)) return default;
            return JsonSerializer.Deserialize<T>(text, _JsonOptions);
        }

        /// <summary>
        /// Send an authenticated request and deserialize a required JSON response.
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path beginning with a slash, without query.</param>
        /// <param name="body">Optional body serialized as JSON.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deserialized response.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server returned an empty body.</exception>
        public async Task<T> SendRequiredAsync<T>(HttpMethod method, string path, object? body = null, IDictionary<string, string?>? query = null, CancellationToken token = default)
        {
            T? result = await SendAsync<T>(method, path, body, query, token).ConfigureAwait(false);
            if (result == null) throw new InvalidOperationException("The server returned an empty response for " + method.Method + " " + path + ".");
            return result;
        }

        /// <summary>
        /// Send a request and return the response text.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path beginning with a slash, without query.</param>
        /// <param name="body">Optional body; strings are sent verbatim, other objects as JSON.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="headers">Optional extra headers; null or empty values are skipped.</param>
        /// <param name="authenticate">Whether to apply the configured authentication.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response text (empty for 204).</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<string> SendTextAsync(HttpMethod method, string path, object? body, IDictionary<string, string?>? query, IDictionary<string, string?>? headers, bool authenticate, CancellationToken token = default)
        {
            int attempt = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (authenticate) await RefreshIfDueAsync(path, token).ConfigureAwait(false);
                TimeSpan? wait = null;
                using (HttpRequestMessage request = Build(method, path, body, query, headers, authenticate))
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return text;
                    if ((int)response.StatusCode != 429 || attempt >= _RateLimitRetries) throw Error(response, text);
                    wait = RetryDelay(response, attempt);
                }
                attempt++;
                await Task.Delay(wait.Value, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send an unauthenticated request with explicit headers and deserialize the JSON response (used for login).
        /// </summary>
        /// <typeparam name="T">Response type.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path.</param>
        /// <param name="body">Optional body.</param>
        /// <param name="headers">Headers to send.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deserialized response.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server returned an empty body.</exception>
        public async Task<T> SendAnonymousAsync<T>(HttpMethod method, string path, object? body, IDictionary<string, string?>? headers, CancellationToken token = default)
        {
            string text = await SendTextAsync(method, path, body, null, headers, false, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The server returned an empty response for " + method.Method + " " + path + ".");
            return JsonSerializer.Deserialize<T>(text, _JsonOptions) ?? throw new InvalidOperationException("The server returned null for " + method.Method + " " + path + ".");
        }

        /// <summary>
        /// Open a server-sent event stream and yield events until the server closes it or the token is cancelled.
        /// </summary>
        /// <param name="method">HTTP method (GET for the events stream, POST for chat).</param>
        /// <param name="path">Path.</param>
        /// <param name="body">Optional JSON body.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="token">Cancellation token. Cancel it to close the stream.</param>
        /// <returns>Events.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async IAsyncEnumerable<SseEvent> StreamAsync(HttpMethod method, string path, object? body = null, IDictionary<string, string?>? query = null, [EnumeratorCancellation] CancellationToken token = default)
        {
            await RefreshIfDueAsync(path, token).ConfigureAwait(false);
            using (HttpRequestMessage request = Build(method, path, body, query, null, true))
            {
                request.Headers.Accept.Clear();
                request.Headers.Accept.ParseAdd("text/event-stream");
                using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw Error(response, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                    using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    {
                        await foreach (SseEvent item in SseReader.ReadAsync(stream, token).ConfigureAwait(false))
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remember a token response: switch to token authentication and store the token, expiry, and tenant.
        /// </summary>
        /// <param name="response">Token response.</param>
        /// <exception cref="ArgumentNullException">Thrown when response is null.</exception>
        public void ApplyToken(TokenResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            AuthMode = AuthModeEnum.Token;
            Token = response.Token;
            TokenExpiresUtc = response.ExpiresUtc;
            if (!String.IsNullOrEmpty(response.TenantId)) TenantId = response.TenantId;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                if (_OwnsHttp) _Http.Dispose();
                _RefreshLock.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task RefreshIfDueAsync(string path, CancellationToken token)
        {
            if (!AutoRefresh || AuthMode != AuthModeEnum.Token || !TokenExpiresUtc.HasValue || String.IsNullOrEmpty(Token)) return;
            if (String.Equals(path, _RefreshPath, StringComparison.Ordinal)) return;
            if (DateTime.UtcNow < TokenExpiresUtc.Value.AddSeconds(-_RefreshSkewSeconds)) return;
            await _RefreshLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!TokenExpiresUtc.HasValue || DateTime.UtcNow < TokenExpiresUtc.Value.AddSeconds(-_RefreshSkewSeconds)) return;
                if (DateTime.UtcNow >= TokenExpiresUtc.Value) return;
                try
                {
                    string text = await SendTextAsync(HttpMethod.Post, _RefreshPath, null, null, null, true, token).ConfigureAwait(false);
                    TokenResponse? refreshed = String.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<TokenResponse>(text, _JsonOptions);
                    if (refreshed != null && !String.IsNullOrEmpty(refreshed.Token)) ApplyToken(refreshed);
                }
                catch (HugoException)
                {
                    // The original request reports the authentication failure.
                }
            }
            finally
            {
                _RefreshLock.Release();
            }
        }

        private HttpRequestMessage Build(HttpMethod method, string path, object? body, IDictionary<string, string?>? query, IDictionary<string, string?>? headers, bool authenticate)
        {
            if (!String.IsNullOrEmpty(TenantOverride)
                && path.StartsWith("/v1.0/api/", StringComparison.Ordinal)
                && !path.StartsWith("/v1.0/api/tenants", StringComparison.Ordinal)
                && (query == null || !query.ContainsKey("tenantId")))
            {
                Dictionary<string, string?> withTenant = query == null ? new Dictionary<string, string?>(StringComparer.Ordinal) : new Dictionary<string, string?>(query, StringComparer.Ordinal);
                withTenant["tenantId"] = TenantOverride;
                query = withTenant;
            }
            string queryString = QueryBuilder.Encode(query);
            HttpRequestMessage request = new HttpRequestMessage(method, _BaseUrl + path + (queryString.Length > 0 ? "?" + queryString : String.Empty));
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.Accept.ParseAdd("application/json");
            byte[]? bytes = null;
            if (body != null)
            {
                bytes = Encoding.UTF8.GetBytes(body is string s ? s : JsonSerializer.Serialize(body, body.GetType(), _JsonOptions));
                ByteArrayContent content = new ByteArrayContent(bytes);
                content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                request.Content = content;
            }
            if (headers != null)
            {
                foreach (KeyValuePair<string, string?> header in headers.Where(h => !String.IsNullOrEmpty(h.Value)))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            if (!authenticate) return request;

            switch (AuthMode)
            {
                case AuthModeEnum.Token:
                    if (!String.IsNullOrEmpty(Token)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Token);
                    break;
                case AuthModeEnum.AccessKey:
                    request.Headers.TryAddWithoutValidation("x-access-key", AccessKey);
                    request.Headers.TryAddWithoutValidation("x-secret-key", SecretKey);
                    if (!String.IsNullOrEmpty(TenantId)) request.Headers.TryAddWithoutValidation("x-tenant-id", TenantId);
                    break;
                case AuthModeEnum.SignedRequest:
                    string date = RequestSigner.FormatDate(DateTime.UtcNow);
                    string nonce = RequestSigner.NewNonce();
                    string canonical = RequestSigner.Canonicalize(method.Method, path, queryString, TenantId, date, nonce, bytes);
                    request.Headers.TryAddWithoutValidation("x-access-key", AccessKey);
                    request.Headers.TryAddWithoutValidation("x-date", date);
                    request.Headers.TryAddWithoutValidation("x-nonce", nonce);
                    request.Headers.TryAddWithoutValidation("x-signature", RequestSigner.Sign(SecretKey ?? String.Empty, canonical));
                    if (!String.IsNullOrEmpty(TenantId)) request.Headers.TryAddWithoutValidation("x-tenant-id", TenantId);
                    break;
                case AuthModeEnum.AdminApiKey:
                    request.Headers.TryAddWithoutValidation("x-api-key", AdminApiKey);
                    if (!String.IsNullOrEmpty(TenantId)) request.Headers.TryAddWithoutValidation("x-tenant-id", TenantId);
                    break;
            }
            return request;
        }

        private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
        {
            double seconds = Math.Pow(2, attempt + 1);
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) seconds = response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                else if (response.Headers.RetryAfter.Date.HasValue) seconds = (response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
            }
            seconds = Math.Clamp(seconds, 1, _RateLimitMaxWaitSeconds);
            return TimeSpan.FromSeconds(seconds);
        }

        private static HugoException Error(HttpResponseMessage response, string text)
        {
            ErrorResponse? error = null;
            try
            {
                if (!String.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("{", StringComparison.Ordinal)) error = JsonSerializer.Deserialize<ErrorResponse>(text, _JsonOptions);
            }
            catch (JsonException)
            {
                error = null;
            }
            string? requestId = null;
            if (response.Headers.TryGetValues("x-request-id", out IEnumerable<string>? values)) requestId = values.FirstOrDefault();
            return new HugoException((int)response.StatusCode, error, text, requestId);
        }

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
