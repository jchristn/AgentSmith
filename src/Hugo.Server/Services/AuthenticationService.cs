namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Specialized;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// Authenticates requests and issues session tokens (P1.19, plan section 15.2). Schemes: the system administrator API
    /// key (x-api-key, only when configured), bearer session tokens (Authorization or x-token, which must agree), signed
    /// credential requests (x-access-key, x-date, x-nonce, x-signature), and direct credential headers (x-access-key,
    /// x-secret-key). Conflicting schemes are rejected. The tenant header (x-tenant-id or x-tenant-guid) must agree with
    /// the principal's tenant. Sessions, tenants, users, and credentials are read through a short-lived cache. Thread-safe.
    /// </summary>
    public class AuthenticationService
    {
        #region Public-Members

        /// <summary>
        /// Cache of the records authentication reads. Identity changes and session revocations clear it.
        /// </summary>
        public AuthCache Cache { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly TokenService _Tokens;
        private readonly SecretCipher _Cipher;
        private readonly HugoSettings _Settings;
        private readonly AuditService _Audit;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="tokens">Token service.</param>
        /// <param name="cipher">Secret cipher (credential secrets).</param>
        /// <param name="settings">Settings.</param>
        /// <param name="audit">Audit service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AuthenticationService(DatabaseDriverBase database, TokenService tokens, SecretCipher cipher, HugoSettings settings, AuditService audit)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            Cache = new AuthCache(database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Watson AuthenticateRequest hook. Rejects unauthenticated requests with 401 and stores the context in metadata.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Task.</returns>
        public async Task AuthenticateRequestAsync(HttpContextBase context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            RequestTelemetry.CaptureSpan(context);
            RequestContext resolved = await ResolveAsync(
                context.Request.Headers,
                context.Request.Method.ToString(),
                context.Request.Url.RawWithoutQuery,
                QueryString(context.Request.Url.RawWithQuery),
                context.Request.DataAsBytes,
                context.Request.Source.IpAddress,
                context.Token).ConfigureAwait(false);

            context.Metadata = resolved;
            if (!resolved.IsAuthenticated)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                ErrorResponse error = new ErrorResponse("Unauthorized", "AuthenticationRequired", "Authentication is required or the supplied credentials are invalid.");
                error.RequestId = resolved.RequestId;
                await context.Response.Send(Serializer.Serialize(error)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Resolve a request into an authenticated context from its headers and body.
        /// </summary>
        /// <param name="headers">Request headers.</param>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path without query.</param>
        /// <param name="query">Raw query without the question mark.</param>
        /// <param name="body">Body bytes, used for signed requests.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Context; IsAuthenticated is false when authentication failed.</returns>
        public async Task<RequestContext> ResolveAsync(NameValueCollection headers, string method, string path, string? query, byte[]? body, string? sourceIp, CancellationToken token = default)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers));
            RequestContext ctx = new RequestContext();
            ctx.SourceIp = sourceIp;
            string? requestId = headers[Constants.HeaderRequestId];
            if (!String.IsNullOrEmpty(requestId) && requestId.Length <= 64) ctx.RequestId = requestId;
            string? tenantHeader = TenantHeader(headers);

            string? apiKey = headers[Constants.HeaderApiKey];
            string? bearer = ExtractBearer(headers["Authorization"]);
            string? xToken = headers[Constants.HeaderToken];
            string? accessKey = headers[Constants.HeaderAccessKey];

            int schemes = (String.IsNullOrEmpty(apiKey) ? 0 : 1) + (String.IsNullOrEmpty(bearer) && String.IsNullOrEmpty(xToken) ? 0 : 1) + (String.IsNullOrEmpty(accessKey) ? 0 : 1);
            if (schemes > 1) return await FailAsync(ctx, AuthSchemeEnum.BearerToken, AuthResultEnum.Invalid, "More than one authentication scheme was presented.", token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(apiKey))
            {
                if (!String.IsNullOrEmpty(_Settings.Auth.AdminApiKey) && PasswordHasher.FixedTimeEquals(apiKey, _Settings.Auth.AdminApiKey))
                {
                    ctx.IsAuthenticated = true;
                    ctx.AuthResult = AuthResultEnum.Success;
                    ctx.AuthScheme = AuthSchemeEnum.AdminApiKey;
                    ctx.PrincipalType = PrincipalTypeEnum.Administrator;
                    ctx.PrincipalName = "System Administrator (API key)";
                    ctx.IsAdmin = true;
                    ctx.TenantId = tenantHeader;
                    Succeeded(AuthSchemeEnum.AdminApiKey);
                    return ctx;
                }
                return await FailAsync(ctx, AuthSchemeEnum.AdminApiKey, AuthResultEnum.Invalid, String.IsNullOrEmpty(_Settings.Auth.AdminApiKey) ? "The administrator API key scheme is disabled." : "Invalid administrator API key.", token).ConfigureAwait(false);
            }

            if (!String.IsNullOrEmpty(bearer) && !String.IsNullOrEmpty(xToken) && !PasswordHasher.FixedTimeEquals(bearer, xToken))
            {
                return await FailAsync(ctx, AuthSchemeEnum.BearerToken, AuthResultEnum.Invalid, "Authorization and x-token carry different tokens.", token).ConfigureAwait(false);
            }
            string? sessionToken = !String.IsNullOrEmpty(bearer) ? bearer : xToken;
            if (!String.IsNullOrEmpty(sessionToken))
            {
                RequestContext session = await ResolveSessionTokenAsync(ctx, sessionToken, String.IsNullOrEmpty(bearer) ? AuthSchemeEnum.XToken : AuthSchemeEnum.BearerToken, token).ConfigureAwait(false);
                if (session.IsAuthenticated && !String.IsNullOrEmpty(tenantHeader) && !session.IsAdmin && !String.Equals(tenantHeader, session.TenantId, StringComparison.Ordinal))
                {
                    return await FailAsync(ctx, session.AuthScheme ?? AuthSchemeEnum.BearerToken, AuthResultEnum.Invalid, "Tenant header does not match the session tenant.", token).ConfigureAwait(false);
                }
                return session;
            }

            if (!String.IsNullOrEmpty(accessKey))
            {
                string? signature = headers[Constants.HeaderSignature];
                string? secret = headers[Constants.HeaderSecretKey];
                if (!String.IsNullOrEmpty(signature) && !String.IsNullOrEmpty(secret))
                {
                    return await FailAsync(ctx, AuthSchemeEnum.AccessKeySignature, AuthResultEnum.Invalid, "Supply either x-signature or x-secret-key, not both.", token).ConfigureAwait(false);
                }

                Credential? credential = await Cache.CredentialByAccessKeyAsync(accessKey, token).ConfigureAwait(false);
                if (credential == null) return await FailAsync(ctx, AuthSchemeEnum.AccessKeySecret, AuthResultEnum.NotFound, "Unknown access key.", token).ConfigureAwait(false);

                if (!String.IsNullOrEmpty(tenantHeader) && tenantHeader != credential.TenantId)
                {
                    return await FailAsync(ctx, AuthSchemeEnum.AccessKeySecret, AuthResultEnum.Invalid, "Tenant header does not match the credential tenant.", token).ConfigureAwait(false);
                }

                string storedSecret = DecryptCredentialSecret(credential);
                if (!String.IsNullOrEmpty(signature))
                {
                    if (credential.AuthMode == CredentialAuthModeEnum.DirectHeader)
                    {
                        return await FailAsync(ctx, AuthSchemeEnum.AccessKeySignature, AuthResultEnum.Invalid, "The credential does not allow signed requests.", token).ConfigureAwait(false);
                    }
                    string? date = headers[Constants.HeaderDate];
                    string? nonce = headers[Constants.HeaderNonce];
                    if (!RequestSigner.DateWithinSkew(date, _Settings.Auth.SignedRequestSkewSeconds) || String.IsNullOrEmpty(nonce))
                    {
                        return await FailAsync(ctx, AuthSchemeEnum.AccessKeySignature, AuthResultEnum.Invalid, "Signed request date is outside the allowed skew or the nonce is missing.", token).ConfigureAwait(false);
                    }
                    string canonical = RequestSigner.Canonicalize(method, path, query, credential.TenantId, date!, nonce, body);
                    string expected = RequestSigner.Sign(storedSecret, canonical);
                    if (!PasswordHasher.FixedTimeEquals(expected, signature.ToLowerInvariant()))
                    {
                        return await FailAsync(ctx, AuthSchemeEnum.AccessKeySignature, AuthResultEnum.Invalid, "Signature mismatch.", token).ConfigureAwait(false);
                    }
                    // A request is accepted while its date is within the skew either side of now, so a nonce must be
                    // remembered for twice the skew after its first use.
                    DateTime nonceExpires = DateTime.UtcNow.AddSeconds(2 * _Settings.Auth.SignedRequestSkewSeconds + 5);
                    if (!await _Database.Nonces.TryUseAsync(accessKey + ":" + nonce, nonceExpires, token).ConfigureAwait(false))
                    {
                        return await FailAsync(ctx, AuthSchemeEnum.AccessKeySignature, AuthResultEnum.Invalid, "Nonce was already used.", token).ConfigureAwait(false);
                    }
                    return await BuildCredentialContextAsync(ctx, credential, AuthSchemeEnum.AccessKeySignature, token).ConfigureAwait(false);
                }

                if (credential.AuthMode == CredentialAuthModeEnum.SignedRequest)
                {
                    return await FailAsync(ctx, AuthSchemeEnum.AccessKeySecret, AuthResultEnum.Invalid, "The credential requires signed requests.", token).ConfigureAwait(false);
                }
                if (String.IsNullOrEmpty(secret) || !PasswordHasher.FixedTimeEquals(secret, storedSecret))
                {
                    return await FailAsync(ctx, AuthSchemeEnum.AccessKeySecret, AuthResultEnum.Invalid, "Invalid secret key.", token).ConfigureAwait(false);
                }
                return await BuildCredentialContextAsync(ctx, credential, AuthSchemeEnum.AccessKeySecret, token).ConfigureAwait(false);
            }

            ctx.AuthResult = AuthResultEnum.NotFound;
            HugoMetrics.Count(HugoMetricCatalog.AuthFailures, "scheme", "None", "reason", "Missing");
            HugoMetrics.Count(HugoMetricCatalog.AuthRequests, "scheme", "None", "result", "Missing");
            return ctx;
        }

        /// <summary>
        /// Authenticate a user by tenant, email, and password. Hashes with fewer iterations than configured are upgraded.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="email">Email.</param>
        /// <param name="password">Password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User or null.</returns>
        public async Task<User?> AuthenticateUserAsync(string? tenantId, string? email, string? password, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(email) || String.IsNullOrEmpty(password)) return null;
            Tenant? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            User? user = (tenant != null && tenant.Active) ? await _Database.Users.ReadByEmailAsync(tenantId, email, token).ConfigureAwait(false) : null;
            if (user == null || !user.Active)
            {
                // Spend the time of a real verification so the response time does not reveal which accounts exist.
                PasswordHasher.VerifyDummy(password);
                return null;
            }
            if (!PasswordHasher.Verify(password, user.PasswordHash)) return null;

            int iterations = _Settings.Auth.PasswordHashIterations;
            if (PasswordHasher.NeedsRehash(user.PasswordHash, iterations))
            {
                user.PasswordHash = PasswordHasher.Hash(password, iterations);
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
            return user;
        }

        /// <summary>
        /// Authenticate a credential by access key and secret key.
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential or null.</returns>
        public async Task<Credential?> AuthenticateCredentialAsync(string? accessKey, string? secretKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(accessKey) || String.IsNullOrEmpty(secretKey)) return null;
            Credential? credential = await Cache.CredentialByAccessKeyAsync(accessKey, token).ConfigureAwait(false);
            if (credential == null || !await CredentialUsableAsync(credential, token).ConfigureAwait(false)) return null;
            if (!PasswordHasher.FixedTimeEquals(secretKey, DecryptCredentialSecret(credential))) return null;
            return credential;
        }

        /// <summary>
        /// Issue a session token for a user.
        /// </summary>
        /// <param name="user">User.</param>
        /// <param name="scheme">Scheme that authenticated the user.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="userAgent">User agent.</param>
        /// <param name="absoluteExpiresUtc">Refresh limit carried from an earlier session, or null for a new sign-in.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when user is null.</exception>
        public async Task<TokenResponse> IssueForUserAsync(User user, AuthSchemeEnum scheme, string? sourceIp, string? userAgent, DateTime? absoluteExpiresUtc = null, CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            DateTime absolute = absoluteExpiresUtc ?? DateTime.UtcNow.AddHours(_Settings.Auth.SessionAbsoluteMaxHours);
            int minutes = user.IsAdmin ? _Settings.Auth.AdminSessionMinutes : _Settings.Auth.UserSessionMinutes;
            DateTime expires = Min(DateTime.UtcNow.AddMinutes(minutes), absolute);
            AuthSession session = NewSession(user.TenantId, PrincipalTypeEnum.User, scheme, sourceIp, userAgent, expires);
            session.UserId = user.Id;
            await _Database.Sessions.CreateAsync(session, token).ConfigureAwait(false);
            TokenPayload payload = Payload(session, absolute);
            payload.UserId = user.Id;
            await _Audit.WriteAsync(null, AuditEventTypeEnum.SessionIssued, "Session", session.Id, "Session issued to user " + user.Email + ".", user.TenantId, token).ConfigureAwait(false);
            HugoMetrics.Count(HugoMetricCatalog.AuthTokensIssued, "scheme", scheme.ToString(), "principal", PrincipalTypeEnum.User.ToString());
            TokenResponse response = Response(session, payload, user.TenantId);
            response.MustChangePassword = user.MustChangePassword;
            return response;
        }

        /// <summary>
        /// Issue a session token for a credential.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <param name="scheme">Scheme that authenticated the credential.</param>
        /// <param name="sourceIp">Source IP.</param>
        /// <param name="userAgent">User agent.</param>
        /// <param name="absoluteExpiresUtc">Refresh limit carried from an earlier session, or null for a new sign-in.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Issued token response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when credential is null.</exception>
        public async Task<TokenResponse> IssueForCredentialAsync(Credential credential, AuthSchemeEnum scheme, string? sourceIp, string? userAgent, DateTime? absoluteExpiresUtc = null, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            DateTime absolute = absoluteExpiresUtc ?? DateTime.UtcNow.AddHours(_Settings.Auth.SessionAbsoluteMaxHours);
            DateTime expires = Min(DateTime.UtcNow.AddMinutes(_Settings.Auth.UserSessionMinutes), absolute);
            if (credential.ExpiresUtc.HasValue) expires = Min(expires, credential.ExpiresUtc.Value);
            AuthSession session = NewSession(credential.TenantId, PrincipalTypeEnum.Credential, scheme, sourceIp, userAgent, expires);
            session.CredentialId = credential.Id;
            await _Database.Sessions.CreateAsync(session, token).ConfigureAwait(false);
            TokenPayload payload = Payload(session, absolute);
            payload.CredentialId = credential.Id;
            await _Audit.WriteAsync(null, AuditEventTypeEnum.SessionIssued, "Session", session.Id, "Session issued to credential " + credential.Name + ".", credential.TenantId, token).ConfigureAwait(false);
            HugoMetrics.Count(HugoMetricCatalog.AuthTokensIssued, "scheme", scheme.ToString(), "principal", PrincipalTypeEnum.Credential.ToString());
            return Response(session, payload, credential.TenantId);
        }

        /// <summary>
        /// Validate a token string and return its payload, without loading the principal.
        /// </summary>
        /// <param name="tokenString">Token.</param>
        /// <returns>Payload or null.</returns>
        public TokenPayload? Decode(string? tokenString)
        {
            return _Tokens.Validate(tokenString);
        }

        /// <summary>
        /// Revoke the session behind a token.
        /// </summary>
        /// <param name="tokenString">Token.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when revoked.</returns>
        public async Task<bool> RevokeAsync(string? tokenString, string reason, CancellationToken token = default)
        {
            TokenPayload? payload = _Tokens.Validate(tokenString);
            if (payload == null) return false;
            return await RevokeSessionAsync(payload.SessionId, payload.TenantId, reason, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Revoke a session by identifier.
        /// </summary>
        /// <param name="sessionId">Session identifier.</param>
        /// <param name="tenantId">Tenant identifier for the audit record.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when revoked.</returns>
        public async Task<bool> RevokeSessionAsync(string sessionId, string? tenantId, string reason, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sessionId)) return false;
            bool revoked = await _Database.Sessions.RevokeAsync(sessionId, reason, token).ConfigureAwait(false);
            Cache.InvalidateSession(sessionId);
            if (revoked) await _Audit.WriteAsync(null, AuditEventTypeEnum.SessionRevoked, "Session", sessionId, reason, tenantId, token).ConfigureAwait(false);
            return revoked;
        }

        /// <summary>
        /// Record a successful password re-entry on the caller's session (POST /v1.0/token/reauthenticate). Secret reveal
        /// requires one within Secrets.RevealReauthMinutes.
        /// </summary>
        /// <param name="ctx">Authenticated request context with a user session.</param>
        /// <param name="password">Password.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The re-authentication time (UTC), or null when the password is wrong or the request has no user session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when ctx is null.</exception>
        public async Task<DateTime?> ReauthenticateAsync(RequestContext ctx, string? password, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.User == null || String.IsNullOrEmpty(ctx.SessionId)) return null;
            User? user = await AuthenticateUserAsync(ctx.User.TenantId, ctx.User.Email, password, token).ConfigureAwait(false);
            if (user == null || user.Id != ctx.User.Id)
            {
                await FailAsync(new RequestContext { RequestId = ctx.RequestId, SourceIp = ctx.SourceIp }, AuthSchemeEnum.PasswordHeaders, AuthResultEnum.Invalid, "Re-authentication failed for " + ctx.User.Email + ".", token).ConfigureAwait(false);
                return null;
            }
            AuthSession? session = await _Database.Sessions.ReadAsync(ctx.SessionId!, token).ConfigureAwait(false);
            if (session == null) return null;
            DateTime now = DateTime.UtcNow;
            session.ReauthenticatedUtc = now;
            await _Database.Sessions.UpdateAsync(session, token).ConfigureAwait(false);
            Cache.InvalidateSession(session.Id);
            ctx.Session = session;
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.SessionReauthenticated, "Session", session.Id, "Session re-authenticated by " + ctx.User.Email + ".", ctx.TenantId, token).ConfigureAwait(false);
            return now;
        }

        /// <summary>
        /// Encrypt a credential secret key.
        /// </summary>
        /// <param name="secretKey">Secret key.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <returns>Ciphertext.</returns>
        public string EncryptCredentialSecret(string secretKey, string credentialId)
        {
            return _Cipher.EncryptCredential(secretKey, credentialId);
        }

        /// <summary>
        /// Decrypt a credential's stored secret.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <returns>Secret, or empty when it cannot be decrypted.</returns>
        public string DecryptCredentialSecret(Credential credential)
        {
            if (credential == null || String.IsNullOrEmpty(credential.SecretKeyEncrypted)) return String.Empty;
            try
            {
                return _Cipher.DecryptCredential(credential.SecretKeyEncrypted, credential.Id);
            }
            catch (CryptographicException)
            {
                return String.Empty;
            }
            catch (FormatException)
            {
                return String.Empty;
            }
        }

        /// <summary>
        /// Read the tenant header (x-tenant-id, or its alias x-tenant-guid).
        /// </summary>
        /// <param name="headers">Headers.</param>
        /// <returns>Tenant identifier, or null.</returns>
        public static string? TenantHeader(NameValueCollection headers)
        {
            if (headers == null) return null;
            string? value = headers[Constants.HeaderTenantId];
            if (String.IsNullOrEmpty(value)) value = headers[Constants.HeaderTenantGuid];
            return String.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Extract the bearer token from an Authorization header.
        /// </summary>
        /// <param name="authorization">Header value.</param>
        /// <returns>Token, or null.</returns>
        public static string? ExtractBearer(string? authorization)
        {
            if (String.IsNullOrEmpty(authorization)) return null;
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return authorization.Substring(7).Trim();
            return null;
        }

        #endregion

        #region Private-Methods

        private async Task<RequestContext> ResolveSessionTokenAsync(RequestContext ctx, string sessionToken, AuthSchemeEnum scheme, CancellationToken token)
        {
            TokenPayload? payload = _Tokens.Validate(sessionToken);
            if (payload == null || payload.Purpose != null) return await FailAsync(ctx, scheme, AuthResultEnum.Invalid, "Token is invalid or expired.", token).ConfigureAwait(false);

            AuthSession? session = String.IsNullOrEmpty(payload.SessionId) || String.IsNullOrEmpty(payload.TokenId) ? null : await Cache.SessionAsync(payload.SessionId, payload.TokenId, token).ConfigureAwait(false);
            if (session == null || session.Id != payload.SessionId) return await FailAsync(ctx, scheme, AuthResultEnum.NotFound, "Session not found.", token).ConfigureAwait(false);
            if (!session.Active || session.RevokedUtc.HasValue || session.ExpiresUtc < DateTime.UtcNow) return await FailAsync(ctx, scheme, AuthResultEnum.Inactive, "Session is revoked or expired.", token).ConfigureAwait(false);
            if (session.TenantId != payload.TenantId) return await FailAsync(ctx, scheme, AuthResultEnum.Invalid, "Session tenant mismatch.", token).ConfigureAwait(false);

            if (!String.IsNullOrEmpty(session.TenantId))
            {
                Tenant? tenant = await Cache.TenantAsync(session.TenantId, token).ConfigureAwait(false);
                if (tenant == null || !tenant.Active) return await FailAsync(ctx, scheme, AuthResultEnum.Inactive, "Tenant is inactive.", token).ConfigureAwait(false);
            }

            ctx.SessionId = session.Id;
            ctx.Session = session;

            if (payload.PrincipalType == PrincipalTypeEnum.User && !String.IsNullOrEmpty(payload.UserId) && !String.IsNullOrEmpty(payload.TenantId))
            {
                User? user = await Cache.UserAsync(payload.TenantId, payload.UserId, token).ConfigureAwait(false);
                if (user == null || !user.Active) return await FailAsync(ctx, scheme, AuthResultEnum.Inactive, "User is inactive.", token).ConfigureAwait(false);
                ctx.IsAuthenticated = true;
                ctx.AuthResult = AuthResultEnum.Success;
                ctx.AuthScheme = scheme;
                ctx.PrincipalType = PrincipalTypeEnum.User;
                ctx.TenantId = user.TenantId;
                ctx.UserId = user.Id;
                ctx.User = user;
                ctx.PrincipalName = user.Email;
                ctx.IsAdmin = user.IsAdmin;
                ctx.IsTenantAdmin = user.IsTenantAdmin;
                Succeeded(scheme);
                return ctx;
            }

            if (payload.PrincipalType == PrincipalTypeEnum.Credential && !String.IsNullOrEmpty(payload.CredentialId) && !String.IsNullOrEmpty(payload.TenantId))
            {
                Credential? credential = await Cache.CredentialAsync(payload.TenantId, payload.CredentialId, token).ConfigureAwait(false);
                if (credential == null) return await FailAsync(ctx, scheme, AuthResultEnum.NotFound, "Credential not found.", token).ConfigureAwait(false);
                return await BuildCredentialContextAsync(ctx, credential, scheme, token).ConfigureAwait(false);
            }

            return await FailAsync(ctx, scheme, AuthResultEnum.Invalid, "Token principal is not recognized.", token).ConfigureAwait(false);
        }

        private async Task<RequestContext> BuildCredentialContextAsync(RequestContext ctx, Credential credential, AuthSchemeEnum scheme, CancellationToken token)
        {
            if (!await CredentialUsableAsync(credential, token).ConfigureAwait(false))
            {
                return await FailAsync(ctx, scheme, AuthResultEnum.Inactive, "Credential, owner, or tenant is inactive or expired.", token).ConfigureAwait(false);
            }

            User? owner = String.IsNullOrEmpty(credential.TenantId) || String.IsNullOrEmpty(credential.UserId) ? null : await Cache.UserAsync(credential.TenantId, credential.UserId, token).ConfigureAwait(false);
            await Cache.TouchCredentialAsync(credential.Id, token).ConfigureAwait(false);

            ctx.IsAuthenticated = true;
            ctx.AuthResult = AuthResultEnum.Success;
            ctx.AuthScheme = scheme;
            ctx.PrincipalType = PrincipalTypeEnum.Credential;
            ctx.TenantId = credential.TenantId;
            ctx.CredentialId = credential.Id;
            ctx.UserId = credential.UserId;
            ctx.Credential = credential;
            ctx.User = owner;
            ctx.PrincipalName = credential.Name;
            ctx.IsAdmin = owner != null && owner.IsAdmin;
            ctx.IsTenantAdmin = owner != null && owner.IsTenantAdmin;
            Succeeded(scheme);
            return ctx;
        }

        private async Task<bool> CredentialUsableAsync(Credential credential, CancellationToken token)
        {
            if (!credential.Active) return false;
            if (credential.ExpiresUtc.HasValue && credential.ExpiresUtc.Value < DateTime.UtcNow) return false;
            if (String.IsNullOrEmpty(credential.TenantId) || String.IsNullOrEmpty(credential.UserId)) return false;
            Tenant? tenant = await Cache.TenantAsync(credential.TenantId, token).ConfigureAwait(false);
            if (tenant == null || !tenant.Active) return false;
            User? owner = await Cache.UserAsync(credential.TenantId, credential.UserId, token).ConfigureAwait(false);
            return owner != null && owner.Active;
        }

        private static void Succeeded(AuthSchemeEnum scheme)
        {
            HugoMetrics.Count(HugoMetricCatalog.AuthRequests, "scheme", scheme.ToString(), "result", AuthResultEnum.Success.ToString());
        }

        private async Task<RequestContext> FailAsync(RequestContext ctx, AuthSchemeEnum scheme, AuthResultEnum result, string reason, CancellationToken token)
        {
            ctx.IsAuthenticated = false;
            ctx.AuthResult = result;
            ctx.AuthScheme = scheme;
            HugoMetrics.Count(HugoMetricCatalog.AuthFailures, "scheme", scheme.ToString(), "reason", result.ToString());
            HugoMetrics.Count(HugoMetricCatalog.AuthRequests, "scheme", scheme.ToString(), "result", result.ToString());
            AuditRecord record = new AuditRecord();
            record.EventType = AuditEventTypeEnum.AuthFailure;
            record.RequestId = ctx.RequestId;
            record.AuthScheme = scheme;
            record.AuthResult = result;
            record.SourceIp = ctx.SourceIp;
            record.Reason = reason;
            record.StatusCode = 401;
            await _Audit.WriteAsync(record, token).ConfigureAwait(false);
            return ctx;
        }

        private static AuthSession NewSession(string? tenantId, PrincipalTypeEnum principal, AuthSchemeEnum scheme, string? sourceIp, string? userAgent, DateTime expiresUtc)
        {
            AuthSession session = new AuthSession();
            session.TenantId = tenantId;
            session.PrincipalType = principal;
            session.AuthScheme = scheme;
            session.TokenId = SecretGenerator.Alphanumeric(40);
            session.SourceIp = sourceIp;
            session.UserAgent = userAgent != null && userAgent.Length > 500 ? userAgent.Substring(0, 500) : userAgent;
            session.ExpiresUtc = expiresUtc;
            return session;
        }

        private static TokenPayload Payload(AuthSession session, DateTime absoluteExpiresUtc)
        {
            TokenPayload payload = new TokenPayload();
            payload.SessionId = session.Id;
            payload.TokenId = session.TokenId;
            payload.PrincipalType = session.PrincipalType;
            payload.TenantId = session.TenantId;
            payload.IssuedUtc = DateTime.UtcNow;
            payload.ExpiresUtc = session.ExpiresUtc;
            payload.AbsoluteExpiresUtc = absoluteExpiresUtc;
            return payload;
        }

        private TokenResponse Response(AuthSession session, TokenPayload payload, string? tenantId)
        {
            TokenResponse response = new TokenResponse();
            response.Token = _Tokens.Issue(payload);
            response.ExpiresUtc = session.ExpiresUtc;
            response.TenantId = tenantId;
            response.SessionId = session.Id;
            response.PrincipalType = session.PrincipalType;
            return response;
        }

        private static DateTime Min(DateTime a, DateTime b)
        {
            return a < b ? a : b;
        }

        private static string? QueryString(string? rawWithQuery)
        {
            if (String.IsNullOrEmpty(rawWithQuery)) return null;
            int index = rawWithQuery.IndexOf('?');
            return index < 0 ? null : rawWithQuery.Substring(index + 1);
        }

        #endregion
    }
}
