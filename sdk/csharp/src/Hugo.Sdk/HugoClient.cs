namespace Hugo.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Clients;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Entry point of the Hugo SDK. Choose an authentication method (LoginAsync, LoginWithAccessKeyAsync, UseToken,
    /// UseAccessKey, UseSignedRequests, or UseAdminApiKey), then use the module properties. Thread-safe for concurrent
    /// requests; change credentials only between requests.
    /// </summary>
    public class HugoClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// SDK version, mirrored from the product version.
        /// </summary>
        public static readonly string SdkVersion = "0.1.0-alpha";

        /// <summary>
        /// Default base URL.
        /// </summary>
        public static readonly string DefaultBaseUrl = "http://127.0.0.1:8800";

        /// <summary>
        /// Transport shared by every module.
        /// </summary>
        public HugoTransport Transport { get; }

        /// <summary>
        /// Tenant identifier used for tenant-scoped APIs. Set automatically by login; set it explicitly when using an
        /// administrator API key or to address another tenant as a system administrator.
        /// </summary>
        public string? TenantId
        {
            get
            {
                return Transport.TenantId;
            }
            set
            {
                Transport.TenantId = value;
            }
        }

        /// <summary>
        /// Current authentication mode.
        /// </summary>
        public AuthModeEnum AuthMode
        {
            get
            {
                return Transport.AuthMode;
            }
        }

        /// <summary>
        /// Current session token, when authenticated with a token.
        /// </summary>
        public string? Token
        {
            get
            {
                return Transport.Token;
            }
        }

        /// <summary>
        /// Health, server information, observability status, master key, and restart.
        /// </summary>
        public SystemClient System { get; }

        /// <summary>
        /// Tenants.
        /// </summary>
        public TenantsClient Tenants { get; }

        /// <summary>
        /// Users and the signed-in user.
        /// </summary>
        public UsersClient Users { get; }

        /// <summary>
        /// API credentials.
        /// </summary>
        public CredentialsClient Credentials { get; }

        /// <summary>
        /// Roles.
        /// </summary>
        public RolesClient Roles { get; }

        /// <summary>
        /// Permissions.
        /// </summary>
        public PermissionsClient Permissions { get; }

        /// <summary>
        /// Role to permission mappings.
        /// </summary>
        public RolePermissionsClient RolePermissions { get; }

        /// <summary>
        /// User role assignments.
        /// </summary>
        public UserRoleAssignmentsClient UserRoleAssignments { get; }

        /// <summary>
        /// Credential scope assignments.
        /// </summary>
        public CredentialScopeAssignmentsClient CredentialScopeAssignments { get; }

        /// <summary>
        /// Authentication sessions.
        /// </summary>
        public SessionsClient Sessions { get; }

        /// <summary>
        /// Audit records.
        /// </summary>
        public AuditClient Audit { get; }

        /// <summary>
        /// Secrets vault.
        /// </summary>
        public SecretsClient Secrets { get; }

        /// <summary>
        /// Prompt templates.
        /// </summary>
        public PromptTemplatesClient PromptTemplates { get; }

        /// <summary>
        /// Layered prompt templates by key (persona, connector instructions, system templates).
        /// </summary>
        public PromptLayersClient PromptLayers { get; }

        /// <summary>
        /// Request history.
        /// </summary>
        public RequestHistoryClient RequestHistory { get; }

        /// <summary>
        /// Server-sent event stream.
        /// </summary>
        public EventsClient Events { get; }

        /// <summary>
        /// Server settings.
        /// </summary>
        public SettingsClient Settings { get; }

        /// <summary>
        /// Connectors.
        /// </summary>
        public ConnectorsClient Connectors { get; }

        /// <summary>
        /// Resources and resource knowledge.
        /// </summary>
        public ResourcesClient Resources { get; }

        /// <summary>
        /// Agent profiles and bindings.
        /// </summary>
        public AgentProfilesClient AgentProfiles { get; }

        /// <summary>
        /// Model endpoints.
        /// </summary>
        public ModelEndpointsClient ModelEndpoints { get; }

        /// <summary>
        /// Tool invocations, dry run, and direct invoke.
        /// </summary>
        public ToolInvocationsClient ToolInvocations { get; }

        /// <summary>
        /// Approvals.
        /// </summary>
        public ApprovalsClient Approvals { get; }

        /// <summary>
        /// Tenant lockdown.
        /// </summary>
        public LockdownClient Lockdown { get; }

        /// <summary>
        /// Conversations and chat streaming (preview: verified when the Phase 7 conversation routes land).
        /// </summary>
        public ConversationsClient Conversations { get; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseUrl">Base URL. Default http://127.0.0.1:8800. Use 127.0.0.1 rather than localhost for loopback.</param>
        /// <param name="httpClient">Optional HttpClient; when null the client owns one.</param>
        public HugoClient(string? baseUrl = null, HttpClient? httpClient = null)
        {
            Transport = new HugoTransport(String.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl!, httpClient);
            System = new SystemClient(Transport);
            Tenants = new TenantsClient(Transport);
            Users = new UsersClient(Transport);
            Credentials = new CredentialsClient(Transport);
            Roles = new RolesClient(Transport);
            Permissions = new PermissionsClient(Transport);
            RolePermissions = new RolePermissionsClient(Transport);
            UserRoleAssignments = new UserRoleAssignmentsClient(Transport);
            CredentialScopeAssignments = new CredentialScopeAssignmentsClient(Transport);
            Sessions = new SessionsClient(Transport);
            Audit = new AuditClient(Transport);
            Secrets = new SecretsClient(Transport);
            PromptTemplates = new PromptTemplatesClient(Transport);
            PromptLayers = new PromptLayersClient(Transport);
            RequestHistory = new RequestHistoryClient(Transport);
            Events = new EventsClient(Transport);
            Settings = new SettingsClient(Transport);
            Connectors = new ConnectorsClient(Transport);
            Resources = new ResourcesClient(Transport);
            AgentProfiles = new AgentProfilesClient(Transport);
            ModelEndpoints = new ModelEndpointsClient(Transport);
            ToolInvocations = new ToolInvocationsClient(Transport);
            Approvals = new ApprovalsClient(Transport);
            Lockdown = new LockdownClient(Transport);
            Conversations = new ConversationsClient(Transport);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sign in with email and password and use the issued session token for later requests. When the response says
        /// MustChangePassword, call ChangePasswordAsync before anything else (other routes answer 403 PasswordChangeRequired).
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="password">Password.</param>
        /// <param name="tenantId">Tenant identifier; null uses the server's default tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when email or password is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (401 InvalidCredentials, 429 RateLimited).</exception>
        public async Task<TokenResponse> LoginAsync(string email, string password, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));
            if (String.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            Dictionary<string, string?> headers = new Dictionary<string, string?>
            {
                { "x-email", email },
                { "x-password", password },
                { "x-tenant-id", tenantId }
            };
            TokenResponse response = await Transport.SendAnonymousAsync<TokenResponse>(HttpMethod.Post, "/v1.0/token", null, headers, token).ConfigureAwait(false);
            Transport.ApplyToken(response);
            return response;
        }

        /// <summary>
        /// Exchange a credential's access key and secret key for a session token (credentials in DirectHeader,
        /// SessionExchange, or Hybrid mode) and use it for later requests.
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a key is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<TokenResponse> LoginWithAccessKeyAsync(string accessKey, string secretKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            Dictionary<string, string?> headers = new Dictionary<string, string?>
            {
                { "x-access-key", accessKey },
                { "x-secret-key", secretKey }
            };
            TokenResponse response = await Transport.SendAnonymousAsync<TokenResponse>(HttpMethod.Post, "/v1.0/token", null, headers, token).ConfigureAwait(false);
            Transport.ApplyToken(response);
            return response;
        }

        /// <summary>
        /// Use an existing session token.
        /// </summary>
        /// <param name="sessionToken">Token.</param>
        /// <param name="tenantId">Tenant identifier for tenant-scoped APIs, or null to keep the current one.</param>
        /// <param name="expiresUtc">Token expiry, UTC, when known (enables automatic refresh).</param>
        /// <exception cref="ArgumentNullException">Thrown when sessionToken is null or empty.</exception>
        public void UseToken(string sessionToken, string? tenantId = null, DateTime? expiresUtc = null)
        {
            if (String.IsNullOrEmpty(sessionToken)) throw new ArgumentNullException(nameof(sessionToken));
            Transport.AuthMode = AuthModeEnum.Token;
            Transport.Token = sessionToken;
            Transport.TokenExpiresUtc = expiresUtc;
            if (!String.IsNullOrEmpty(tenantId)) Transport.TenantId = tenantId;
        }

        /// <summary>
        /// Send the credential's access key and secret key on every request (x-access-key and x-secret-key).
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="tenantId">Tenant identifier of the credential (needed for tenant-scoped APIs).</param>
        /// <exception cref="ArgumentNullException">Thrown when a key is null or empty.</exception>
        public void UseAccessKey(string accessKey, string secretKey, string? tenantId = null)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            Transport.AuthMode = AuthModeEnum.AccessKey;
            Transport.AccessKey = accessKey;
            Transport.SecretKey = secretKey;
            Transport.Token = null;
            Transport.TokenExpiresUtc = null;
            if (!String.IsNullOrEmpty(tenantId)) Transport.TenantId = tenantId;
        }

        /// <summary>
        /// Sign every request with the credential's secret key (HMAC-SHA256 over method, path, query, tenant, date,
        /// nonce, and body hash). The secret key never leaves the process. Requires a credential in SignedRequest or
        /// Hybrid mode.
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="tenantId">Tenant identifier of the credential (part of the signature).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public void UseSignedRequests(string accessKey, string secretKey, string tenantId)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            if (String.IsNullOrEmpty(secretKey)) throw new ArgumentNullException(nameof(secretKey));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            Transport.AuthMode = AuthModeEnum.SignedRequest;
            Transport.AccessKey = accessKey;
            Transport.SecretKey = secretKey;
            Transport.TenantId = tenantId;
            Transport.Token = null;
            Transport.TokenExpiresUtc = null;
        }

        /// <summary>
        /// Use the system administrator API key (x-api-key). The server enables it only when HUGO_ADMIN_API_KEY is set.
        /// </summary>
        /// <param name="apiKey">API key.</param>
        /// <param name="tenantId">Tenant to address (sent as x-tenant-id), or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when apiKey is null or empty.</exception>
        public void UseAdminApiKey(string apiKey, string? tenantId = null)
        {
            if (String.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
            Transport.AuthMode = AuthModeEnum.AdminApiKey;
            Transport.AdminApiKey = apiKey;
            Transport.Token = null;
            Transport.TokenExpiresUtc = null;
            if (!String.IsNullOrEmpty(tenantId)) Transport.TenantId = tenantId;
        }

        /// <summary>
        /// Forget every credential. Later requests are anonymous.
        /// </summary>
        public void ClearAuthentication()
        {
            Transport.AuthMode = AuthModeEnum.None;
            Transport.Token = null;
            Transport.TokenExpiresUtc = null;
            Transport.AccessKey = null;
            Transport.SecretKey = null;
            Transport.AdminApiKey = null;
        }

        /// <summary>
        /// Issue a fresh token for the current session (until Auth.SessionAbsoluteMaxHours after sign-in) and use it.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token response.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (401 SessionExpired after the absolute maximum).</exception>
        public async Task<TokenResponse> RefreshAsync(CancellationToken token = default)
        {
            TokenResponse response = await Transport.SendRequiredAsync<TokenResponse>(HttpMethod.Post, "/v1.0/token/refresh", null, null, token).ConfigureAwait(false);
            Transport.ApplyToken(response);
            return response;
        }

        /// <summary>
        /// Revoke the current token (logout) and clear it from the client.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task LogoutAsync(CancellationToken token = default)
        {
            await Transport.SendTextAsync(HttpMethod.Delete, "/v1.0/token", null, null, null, true, token).ConfigureAwait(false);
            Transport.Token = null;
            Transport.TokenExpiresUtc = null;
            Transport.AuthMode = AuthModeEnum.None;
        }

        /// <summary>
        /// Whether the presented credentials are valid.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validity.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (401 when invalid).</exception>
        public Task<TokenValidResponse> ValidateTokenAsync(CancellationToken token = default)
        {
            return Transport.SendRequiredAsync<TokenValidResponse>(HttpMethod.Get, "/v1.0/token", null, null, token);
        }

        /// <summary>
        /// Describe the authenticated principal.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Details.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<TokenDetails> GetTokenDetailsAsync(CancellationToken token = default)
        {
            return Transport.SendRequiredAsync<TokenDetails>(HttpMethod.Get, "/v1.0/token/details", null, null, token);
        }

        /// <summary>
        /// Re-enter the password on the current user session. Required within Secrets.RevealReauthMinutes before
        /// revealing secrets.
        /// </summary>
        /// <param name="password">Current password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Re-authentication result with the reveal window.</returns>
        /// <exception cref="ArgumentNullException">Thrown when password is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (401 InvalidCredentials).</exception>
        public Task<ReauthenticateResponse> ReauthenticateAsync(string password, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            Dictionary<string, string> body = new Dictionary<string, string> { { "password", password } };
            return Transport.SendRequiredAsync<ReauthenticateResponse>(HttpMethod.Post, "/v1.0/token/reauthenticate", body, null, token);
        }

        /// <summary>
        /// Change the signed-in user's password. Clears must-change-password; other sessions of the user are revoked.
        /// </summary>
        /// <param name="currentPassword">Current password.</param>
        /// <param name="newPassword">New password, 8 to 256 characters, different from the current one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated user.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a password is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 with field errors when invalid).</exception>
        public Task<User> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(currentPassword)) throw new ArgumentNullException(nameof(currentPassword));
            if (String.IsNullOrEmpty(newPassword)) throw new ArgumentNullException(nameof(newPassword));
            PasswordChangeRequest request = new PasswordChangeRequest();
            request.CurrentPassword = currentPassword;
            request.NewPassword = newPassword;
            return Transport.SendRequiredAsync<User>(HttpMethod.Post, "/v1.0/api/users/me/password", request, null, token);
        }

        /// <summary>
        /// Find the tenants an email can sign in to (anonymous).
        /// </summary>
        /// <param name="email">Email.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenants, ordered by name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when email is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<TenantLookupEntry>> LookupTenantsAsync(string email, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));
            Dictionary<string, string> body = new Dictionary<string, string> { { "email", email } };
            return Transport.SendAnonymousAsync<List<TenantLookupEntry>>(HttpMethod.Post, "/v1.0/tenants/lookup", body, null, token);
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
            if (disposing) Transport.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
