namespace Hugo.Cli.Api
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The few Hugo REST calls the CLI makes: token issue, token details, token revocation, and the agent profile list.
    /// Thread-safe.
    /// </summary>
    public class HugoApiClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Server base URL without a trailing slash, for example <c>http://127.0.0.1:8800</c>.
        /// </summary>
        public string Server { get; }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server base URL (absolute http or https).</param>
        /// <param name="httpClient">HTTP client to use, or null to create one (with a 30 second timeout) that this instance disposes.</param>
        /// <exception cref="ArgumentNullException">Thrown when server is null or empty.</exception>
        public HugoApiClient(string server, HttpClient? httpClient = null)
        {
            if (String.IsNullOrEmpty(server)) throw new ArgumentNullException(nameof(server));
            Server = server.TrimEnd('/');
            if (httpClient == null)
            {
                _Http = new HttpClient();
                _Http.Timeout = TimeSpan.FromSeconds(30);
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
        /// Sign in with email and password (<c>POST /v1.0/token</c> with <c>x-email</c>, <c>x-password</c>, and
        /// <c>x-tenant-guid</c>).
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="password">Password. Sent once and never stored.</param>
        /// <param name="tenantId">Tenant ID, or null for the server's default tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when email or password is null or empty.</exception>
        /// <exception cref="HugoApiException">Thrown when the server rejects the sign-in.</exception>
        /// <exception cref="CliException">Thrown when the server cannot be reached or answers with an unexpected body.</exception>
        public async Task<TokenResponse> LoginWithPasswordAsync(string email, string password, string? tenantId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));
            if (String.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
            headers["x-email"] = email;
            headers["x-password"] = password;
            if (!String.IsNullOrEmpty(tenantId)) headers["x-tenant-guid"] = tenantId;
            return await IssueAsync(headers, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Exchange an access key and secret key for a session token (<c>POST /v1.0/token</c> with
        /// <c>x-access-key</c>, <c>x-secret-key</c>, and <c>x-tenant-guid</c>).
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="tenantId">Tenant ID, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a key is null or empty.</exception>
        /// <exception cref="HugoApiException">Thrown when the server rejects the keys.</exception>
        /// <exception cref="CliException">Thrown when the server cannot be reached or answers with an unexpected body.</exception>
        public async Task<TokenResponse> LoginWithAccessKeyAsync(string accessKey, string secretKey, string? tenantId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
            headers["x-access-key"] = accessKey;
            headers["x-secret-key"] = secretKey;
            if (!String.IsNullOrEmpty(tenantId)) headers["x-tenant-guid"] = tenantId;
            return await IssueAsync(headers, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Describe the principal behind a token (<c>GET /v1.0/token/details</c>).
        /// </summary>
        /// <param name="sessionToken">Session token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Details.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the session token is null or empty.</exception>
        /// <exception cref="HugoApiException">Thrown when the server rejects the token.</exception>
        /// <exception cref="CliException">Thrown when the server cannot be reached or answers with an unexpected body.</exception>
        public async Task<TokenDetailsResponse> GetTokenDetailsAsync(string sessionToken, CancellationToken token)
        {
            if (String.IsNullOrEmpty(sessionToken)) throw new ArgumentNullException(nameof(sessionToken));
            string body = await SendAsync(HttpMethod.Get, "/v1.0/token/details", sessionToken, null, token).ConfigureAwait(false);
            TokenDetailsResponse? details = CliJson.TryDeserialize<TokenDetailsResponse>(body);
            if (details == null) throw new CliException("The server answered GET /v1.0/token/details with an unexpected body.");
            return details;
        }

        /// <summary>
        /// List the agent profiles the caller can see (<c>GET /v1.0/api/agent-profiles</c>). Accepts the enumeration
        /// envelope or a bare array.
        /// </summary>
        /// <param name="sessionToken">Session token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profiles, with the raw body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the session token is null or empty.</exception>
        /// <exception cref="HugoApiException">Thrown for any status other than success (404 when the route does not exist).</exception>
        /// <exception cref="CliException">Thrown when the server cannot be reached or answers with an unexpected body.</exception>
        public async Task<AgentProfileListResponse> ListAgentProfilesAsync(string sessionToken, CancellationToken token)
        {
            if (String.IsNullOrEmpty(sessionToken)) throw new ArgumentNullException(nameof(sessionToken));
            string body = await SendAsync(HttpMethod.Get, "/v1.0/api/agent-profiles", sessionToken, null, token).ConfigureAwait(false);
            AgentProfileListResponse? list;
            if (body.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                List<AgentProfileSummary>? items = CliJson.TryDeserialize<List<AgentProfileSummary>>(body);
                list = items == null ? null : new AgentProfileListResponse { Objects = items, TotalRecords = items.Count };
            }
            else
            {
                list = CliJson.TryDeserialize<AgentProfileListResponse>(body);
            }
            if (list == null) throw new CliException("The server answered GET /v1.0/api/agent-profiles with an unexpected body.");
            if (list.Objects == null) list.Objects = new List<AgentProfileSummary>();
            list.RawJson = body;
            return list;
        }

        /// <summary>
        /// Revoke a session token (<c>DELETE /v1.0/token</c>).
        /// </summary>
        /// <param name="sessionToken">Session token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the session token is null or empty.</exception>
        /// <exception cref="HugoApiException">Thrown when the server rejects the request.</exception>
        /// <exception cref="CliException">Thrown when the server cannot be reached.</exception>
        public async Task RevokeAsync(string sessionToken, CancellationToken token)
        {
            if (String.IsNullOrEmpty(sessionToken)) throw new ArgumentNullException(nameof(sessionToken));
            await SendAsync(HttpMethod.Delete, "/v1.0/token", sessionToken, null, token).ConfigureAwait(false);
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
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing && _OwnsHttp) _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<TokenResponse> IssueAsync(Dictionary<string, string> headers, CancellationToken token)
        {
            string body = await SendAsync(HttpMethod.Post, "/v1.0/token", null, headers, token).ConfigureAwait(false);
            TokenResponse? issued = CliJson.TryDeserialize<TokenResponse>(body);
            if (issued == null || String.IsNullOrEmpty(issued.Token)) throw new CliException("The server answered POST /v1.0/token without a token.");
            return issued;
        }

        private async Task<string> SendAsync(HttpMethod method, string path, string? bearer, Dictionary<string, string>? headers, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (HttpRequestMessage request = new HttpRequestMessage(method, Server + path))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("User-Agent", ProductInfo.UserAgent);
                if (!String.IsNullOrEmpty(bearer)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                HttpResponseMessage response;
                try
                {
                    response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new CliException("Cannot reach the Hugo server at " + Server + ": " + e.Message, e);
                }
                catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                {
                    throw new CliException("The Hugo server at " + Server + " did not answer in time.", e);
                }

                using (response)
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return body;
                    int status = (int)response.StatusCode;
                    ApiErrorResponse? error = CliJson.TryDeserialize<ApiErrorResponse>(body);
                    string message = !String.IsNullOrEmpty(error?.Message) ? error!.Message! : "HTTP " + status + " " + response.ReasonPhrase;
                    throw new HugoApiException(status, error?.Code, message);
                }
            }
        }

        #endregion
    }
}
