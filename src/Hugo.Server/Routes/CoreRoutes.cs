namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Health, metrics, token, re-authentication, and tenant lookup routes (P1.5, P1.21, P2.7).
    /// </summary>
    public class CoreRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly AuthRateLimiter _IpLimiter;
        private readonly AuthRateLimiter _IdentityLimiter;
        private readonly int _ExpectedSchemaVersion = Hugo.Core.Database.Ado.SchemaBuilder.LatestVersion;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public CoreRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _IpLimiter = new AuthRateLimiter(_Context.Database, () => _Context.Settings.Auth.RateLimitPerMinute);
            _IpLimiter.Scope = "token-ip";
            _IdentityLimiter = new AuthRateLimiter(_Context.Database, () => _Context.Settings.Auth.RateLimitPerMinute);
            _IdentityLimiter.Scope = "token-identity";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Anonymous(HttpMethod.GET, "/v1.0/api/health", HealthAsync, "Health: 200 when the database answers, the schema is current, and the server is not draining; 503 otherwise", "Health", null, typeof(HealthResponse));
            routes.Anonymous(HttpMethod.GET, "/v1.0/api/health/ready", HealthAsync, "Readiness (same as /v1.0/api/health)", "Health", null, typeof(HealthResponse));
            routes.Anonymous(HttpMethod.GET, "/v1.0/api/health/live", LiveAsync, "Liveness: 200 while the process runs", "Health", null, typeof(HealthResponse));
            routes.Anonymous(HttpMethod.GET, "/metrics", MetricsAsync, "Prometheus metrics, relayed from the metrics listener (404 when Telemetry.ServeMetricsOnServicePort is false)", "Health");
            routes.Anonymous(HttpMethod.POST, "/v1.0/token", IssueAsync, "Issue a session token from user login (x-email, x-password, x-tenant-id) or credential keys (x-access-key, x-secret-key)", "Tokens", typeof(TokenRequest), typeof(TokenResponse));
            routes.Anonymous(HttpMethod.POST, "/v1.0/tenants/lookup", LookupAsync, "Find the tenants an email can sign in to (login form tenant discovery)", "Tokens", typeof(TenantLookupRequest), typeof(List<TenantLookupEntry>));
            routes.Authenticated(HttpMethod.GET, "/v1.0/token", ValidateAsync, "Validate the presented token", "Tokens", null, typeof(TokenValidResponse));
            routes.Authenticated(HttpMethod.GET, "/v1.0/token/details", DetailsAsync, "Describe the authenticated principal", "Tokens", null, typeof(TokenDetailsResponse));
            routes.Authenticated(HttpMethod.POST, "/v1.0/token/refresh", RefreshAsync, "Issue a fresh token for the authenticated principal (until Auth.SessionAbsoluteMaxHours after sign-in)", "Tokens", null, typeof(TokenResponse));
            routes.Authenticated(HttpMethod.POST, "/v1.0/token/reauthenticate", ReauthenticateAsync, "Re-enter the password on the current session (required before revealing secrets)", "Tokens", typeof(ReauthenticateRequest), typeof(ReauthenticateResponse));
            routes.Authenticated(HttpMethod.DELETE, "/v1.0/token", RevokeAsync, "Revoke the presented token (logout)", "Tokens", null, typeof(TokenValidResponse));
        }

        #endregion

        #region Private-Methods

        private async Task MetricsAsync(HttpContextBase context)
        {
            if (!_Context.Settings.Telemetry.ServeMetricsOnServicePort)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Metrics are not served on this port; set Telemetry.ServeMetricsOnServicePort to true or scrape the metrics listener.").ConfigureAwait(false);
                return;
            }
            MetricsScrape? scrape = await _Context.Telemetry.ScrapeAsync(context.Request.RetrieveHeaderValue("Accept"), context.Token).ConfigureAwait(false);
            if (scrape == null)
            {
                await RouteHelpers.ErrorAsync(context, 503, "MetricsUnavailable", "The Prometheus metrics listener is not enabled or did not answer.").ConfigureAwait(false);
                return;
            }
            context.Response.StatusCode = 200;
            context.Response.ContentType = scrape.ContentType;
            await context.Response.Send(scrape.Body, context.Token).ConfigureAwait(false);
        }

        private async Task LiveAsync(HttpContextBase context)
        {
            HealthResponse health = new HealthResponse();
            health.Node = _Context.NodeId;
            health.Version = Constants.ProductVersion;
            health.Status = _Context.IsDraining ? "draining" : "alive";
            health.Database = "unchecked";
            health.ExpectedSchemaVersion = _ExpectedSchemaVersion;
            await RouteHelpers.JsonAsync(context, 200, health).ConfigureAwait(false);
        }

        private async Task HealthAsync(HttpContextBase context)
        {
            HealthResponse health = new HealthResponse();
            health.Node = _Context.NodeId;
            health.Version = Constants.ProductVersion;
            health.ExpectedSchemaVersion = _ExpectedSchemaVersion;
            RetentionService? retention = _Context.TryGet<RetentionService>();
            health.Background = retention == null ? "unknown" : (retention.Healthy ? "ok" : "stalled");
            try
            {
                if (_Context.Settings.Database.ManageSchema)
                {
                    health.SchemaVersion = await _Context.Database.ReadSchemaVersionAsync(context.Token).ConfigureAwait(false);
                    health.Database = "ok";
                }
                else
                {
                    health.Database = await _Context.Database.PingAsync(context.Token).ConfigureAwait(false) ? "ok" : "unreachable";
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                health.Database = "unreachable";
            }
            if (_Context.IsDraining) health.Status = "draining";
            else if (health.Database != "ok") health.Status = "degraded";
            else if (_Context.Settings.Database.ManageSchema && health.SchemaVersion < health.ExpectedSchemaVersion) health.Status = "migrating";
            else health.Status = "healthy";
            await RouteHelpers.JsonAsync(context, health.Status == "healthy" ? 200 : 503, health).ConfigureAwait(false);
        }

        private async Task<bool> RateLimitedAsync(HttpContextBase context, string? identity)
        {
            bool allowed = await _IpLimiter.TryAcquireAsync(context.Request.Source.IpAddress, context.Token).ConfigureAwait(false);
            if (allowed && !String.IsNullOrEmpty(identity)) allowed = await _IdentityLimiter.TryAcquireAsync(identity.ToLowerInvariant(), context.Token).ConfigureAwait(false);
            if (allowed) return false;
            context.Response.Headers["Retry-After"] = "60";
            await RouteHelpers.ErrorAsync(context, 429, "RateLimited", "Too many authentication attempts. Try again in a minute.").ConfigureAwait(false);
            return true;
        }

        private async Task IssueAsync(HttpContextBase context)
        {
            TokenRequest request = RouteHelpers.Body<TokenRequest>(context) ?? new TokenRequest();
            request.TenantId ??= AuthenticationService.TenantHeader(context.Request.Headers);
            request.Email ??= context.Request.Headers[Constants.HeaderEmail];
            request.Password ??= context.Request.Headers[Constants.HeaderPassword];
            request.AccessKey ??= context.Request.Headers[Constants.HeaderAccessKey];
            request.SecretKey ??= context.Request.Headers[Constants.HeaderSecretKey];
            string? userAgent = context.Request.Useragent;

            if (await RateLimitedAsync(context, !String.IsNullOrEmpty(request.AccessKey) ? request.AccessKey : request.Email).ConfigureAwait(false)) return;

            if (!String.IsNullOrEmpty(request.AccessKey))
            {
                Credential? credential = await _Context.Authentication.AuthenticateCredentialAsync(request.AccessKey, request.SecretKey, context.Token).ConfigureAwait(false);
                if (credential == null || (!String.IsNullOrEmpty(request.TenantId) && request.TenantId != credential.TenantId))
                {
                    await RouteHelpers.ErrorAsync(context, 401, "InvalidCredentials", "The access key or secret key is invalid.").ConfigureAwait(false);
                    return;
                }
                if (credential.AuthMode == CredentialAuthModeEnum.SignedRequest)
                {
                    await RouteHelpers.ErrorAsync(context, 401, "InvalidCredentials", "The credential requires signed requests and cannot exchange its keys for a session.").ConfigureAwait(false);
                    return;
                }
                TokenResponse issued = await _Context.Authentication.IssueForCredentialAsync(credential, AuthSchemeEnum.AccessKeySecret, context.Request.Source.IpAddress, userAgent, null, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, issued).ConfigureAwait(false);
                return;
            }

            string? tenantId = request.TenantId;
            if (String.IsNullOrEmpty(tenantId) && !String.IsNullOrEmpty(request.Email)) tenantId = _Context.DefaultTenantId;
            User? user = await _Context.Authentication.AuthenticateUserAsync(tenantId, request.Email, request.Password, context.Token).ConfigureAwait(false);
            if (user == null)
            {
                await RouteHelpers.ErrorAsync(context, 401, "InvalidCredentials", "The tenant, email, or password is invalid.").ConfigureAwait(false);
                return;
            }
            TokenResponse userToken = await _Context.Authentication.IssueForUserAsync(user, AuthSchemeEnum.PasswordHeaders, context.Request.Source.IpAddress, userAgent, null, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, userToken).ConfigureAwait(false);
        }

        private async Task LookupAsync(HttpContextBase context)
        {
            TenantLookupRequest request = RouteHelpers.Body<TenantLookupRequest>(context) ?? new TenantLookupRequest();
            string? email = String.IsNullOrWhiteSpace(request.Email) ? context.Request.Headers[Constants.HeaderEmail] : request.Email;
            if (await RateLimitedAsync(context, email).ConfigureAwait(false)) return;
            List<TenantLookupEntry> result = new List<TenantLookupEntry>();
            if (!String.IsNullOrWhiteSpace(email))
            {
                EnumerationQuery query = new EnumerationQuery();
                query.MaxResults = 1000;
                query.ActiveOnly = true;
                EnumerationResult<Tenant> tenants = await _Context.Database.Tenants.EnumerateAsync(query, context.Token).ConfigureAwait(false);
                foreach (Tenant tenant in tenants.Objects)
                {
                    User? user = await _Context.Database.Users.ReadByEmailAsync(tenant.Id, email.Trim(), context.Token).ConfigureAwait(false);
                    if (user == null || !user.Active) continue;
                    TenantLookupEntry entry = new TenantLookupEntry();
                    entry.TenantId = tenant.Id;
                    entry.Name = tenant.Name;
                    result.Add(entry);
                }
            }
            await RouteHelpers.JsonAsync(context, 200, result.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList()).ConfigureAwait(false);
        }

        private async Task ValidateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            await RouteHelpers.JsonAsync(context, 200, new TokenValidResponse { Valid = ctx.IsAuthenticated }).ConfigureAwait(false);
        }

        private async Task DetailsAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            TokenDetailsResponse details = new TokenDetailsResponse();
            details.PrincipalType = ctx.PrincipalType;
            details.PrincipalId = ctx.PrincipalId;
            details.PrincipalName = ctx.PrincipalName;
            details.TenantId = ctx.TenantId;
            details.UserId = ctx.UserId;
            details.CredentialId = ctx.CredentialId;
            details.SessionId = ctx.SessionId;
            details.AuthScheme = ctx.AuthScheme;
            details.IsAdmin = ctx.IsAdmin;
            details.IsTenantAdmin = ctx.IsTenantAdmin;
            details.ReauthenticatedUtc = ctx.Session?.ReauthenticatedUtc;
            if (ctx.User != null)
            {
                details.Email = ctx.User.Email;
                details.FirstName = ctx.User.FirstName;
                details.LastName = ctx.User.LastName;
                details.MustChangePassword = ctx.PrincipalType == PrincipalTypeEnum.User && ctx.User.MustChangePassword;
            }
            TokenPayload? payload = _Context.Authentication.Decode(RawToken(context));
            if (payload != null) details.ExpiresUtc = payload.ExpiresUtc;
            await RouteHelpers.JsonAsync(context, 200, details).ConfigureAwait(false);
        }

        private async Task RefreshAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            TokenPayload? payload = _Context.Authentication.Decode(RawToken(context));
            DateTime? absolute = payload?.AbsoluteExpiresUtc;
            if (absolute.HasValue && absolute.Value <= DateTime.UtcNow)
            {
                await RouteHelpers.ErrorAsync(context, 401, "SessionExpired", "The session reached its maximum age; sign in again.").ConfigureAwait(false);
                return;
            }
            if (ctx.PrincipalType == PrincipalTypeEnum.User && ctx.User != null)
            {
                TokenResponse issued = await _Context.Authentication.IssueForUserAsync(ctx.User, AuthSchemeEnum.BearerToken, context.Request.Source.IpAddress, context.Request.Useragent, absolute, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, issued).ConfigureAwait(false);
                return;
            }
            if (ctx.PrincipalType == PrincipalTypeEnum.Credential && ctx.Credential != null)
            {
                TokenResponse issued = await _Context.Authentication.IssueForCredentialAsync(ctx.Credential, AuthSchemeEnum.BearerToken, context.Request.Source.IpAddress, context.Request.Useragent, absolute, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, issued).ConfigureAwait(false);
                return;
            }
            await RouteHelpers.ErrorAsync(context, 400, "RefreshNotSupported", "Only user and credential sessions can be refreshed.").ConfigureAwait(false);
        }

        private async Task ReauthenticateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.User == null || String.IsNullOrEmpty(ctx.SessionId))
            {
                await RouteHelpers.ErrorAsync(context, 400, "ReauthenticationNotSupported", "Only user sessions (bearer tokens) can be re-authenticated.").ConfigureAwait(false);
                return;
            }
            if (await RateLimitedAsync(context, ctx.User.Email).ConfigureAwait(false)) return;
            ReauthenticateRequest request = RouteHelpers.Body<ReauthenticateRequest>(context) ?? new ReauthenticateRequest();
            string? password = String.IsNullOrEmpty(request.Password) ? context.Request.Headers[Constants.HeaderPassword] : request.Password;
            DateTime? when = await _Context.Authentication.ReauthenticateAsync(ctx, password, context.Token).ConfigureAwait(false);
            if (!when.HasValue)
            {
                await RouteHelpers.ErrorAsync(context, 401, "InvalidCredentials", "The password is invalid.").ConfigureAwait(false);
                return;
            }
            ReauthenticateResponse response = new ReauthenticateResponse();
            response.ReauthenticatedUtc = when.Value;
            response.RevealAllowedUntilUtc = when.Value.AddMinutes(_Context.Settings.Secrets.RevealReauthMinutes);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task RevokeAsync(HttpContextBase context)
        {
            bool revoked = await _Context.Authentication.RevokeAsync(RawToken(context), "Logout.", context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, new TokenValidResponse { Valid = !revoked }).ConfigureAwait(false);
        }

        private static string? RawToken(HttpContextBase context)
        {
            string? bearer = AuthenticationService.ExtractBearer(context.Request.Headers["Authorization"]);
            return !String.IsNullOrEmpty(bearer) ? bearer : context.Request.Headers[Constants.HeaderToken];
        }

        #endregion
    }
}
