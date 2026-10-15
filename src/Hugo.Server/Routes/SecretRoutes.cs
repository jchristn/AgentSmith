namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Secrets;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Secrets vault routes (P2.6, P2.7, P2.8; plan section 12.2). Values are write-only except through reveal, which
    /// requires the Reveal permission, a reason, a recent re-authentication, and a rate limit; it is audited and its
    /// bodies are never captured in request history.
    /// </summary>
    public class SecretRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/tenants/{tenantId}/secrets";
        private readonly ServerContext _Server;
        private readonly SecretService _Secrets;
        private readonly AuthRateLimiter _RevealIpLimiter;
        private readonly AuthRateLimiter _RevealPrincipalLimiter;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="secrets">Secret service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SecretRoutes(ServerContext server, SecretService secrets)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            _RevealIpLimiter = new AuthRateLimiter(server.Database, () => server.Settings.Secrets.RevealRateLimitPerMinute);
            _RevealIpLimiter.Scope = "reveal-ip";
            _RevealPrincipalLimiter = new AuthRateLimiter(server.Database, () => server.Settings.Secrets.RevealRateLimitPerMinute);
            _RevealPrincipalLimiter.Scope = "reveal-principal";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The reveal route template; request history never captures its bodies.
        /// </summary>
        public static string RevealPath
        {
            get
            {
                return _Base + "/{id}/reveal";
            }
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Secret, OperationTypeEnum.Read, ListAsync, "List secrets (metadata and field names only)", "Secrets", null, typeof(EnumerationResult<Secret>));
            routes.Scoped(HttpMethod.POST, _Base, ResourceTypeEnum.Secret, OperationTypeEnum.Create, CreateAsync, "Create a secret with its field values; returns metadata only", "Secrets", typeof(SecretRequest), typeof(Secret));
            routes.Scoped(HttpMethod.POST, _Base + "/parse", ResourceTypeEnum.Secret, OperationTypeEnum.Create, ParseAsync, "Describe an uploaded SSH key, GCP service account JSON, kubeconfig, or PEM certificate without storing it", "Secrets", typeof(SecretImportRequest), typeof(SecretImportResult));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.Secret, OperationTypeEnum.Read, ReadAsync, "Read secret metadata, field names, fingerprint, and referrers (never values)", "Secrets", null, typeof(Secret));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}", ResourceTypeEnum.Secret, OperationTypeEnum.Update, UpdateAsync, "Update secret name, description, and expiry", "Secrets", typeof(SecretRequest), typeof(Secret));
            routes.Scoped(HttpMethod.PUT, _Base + "/{id}/values", ResourceTypeEnum.Secret, OperationTypeEnum.Update, ValuesAsync, "Replace the supplied field values (omitted fields and the mask placeholder keep stored values)", "Secrets", typeof(SecretValueRequest), typeof(Secret));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}/referrers", ResourceTypeEnum.Secret, OperationTypeEnum.Read, ReferrersAsync, "Resources and model endpoints that use the secret", "Secrets", null, typeof(List<SecretReferrer>));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/reveal", ResourceTypeEnum.Secret, OperationTypeEnum.Reveal, RevealAsync, "Reveal plaintext values (Reveal permission, reason, and re-authentication within Secrets.RevealReauthMinutes required; audited; never captured in request history)", "Secrets", typeof(SecretRevealRequest), typeof(SecretRevealResponse));
            routes.Scoped(HttpMethod.DELETE, _Base + "/{id}", ResourceTypeEnum.Secret, OperationTypeEnum.Delete, DeleteAsync, "Delete a secret (409 while referenced)", "Secrets");
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Server.Database.Secrets.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            SecretRequest request = RouteHelpers.RequiredBody<SecretRequest>(http);
            Secret created = await _Secrets.CreateAsync(RouteHelpers.Tenant(http), ctx.PrincipalType == PrincipalTypeEnum.User ? ctx.UserId : ctx.User?.Id, request, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.SecretCreated, "Secret", created.Id, "Created " + created.Kind + " secret " + created.Name + " with fields " + String.Join(", ", created.Fields) + ".").ConfigureAwait(false);
            Publish("secret.created", created);
            await RouteHelpers.JsonAsync(http, 201, created).ConfigureAwait(false);
        }

        private async Task ParseAsync(HttpContextBase http)
        {
            SecretImportRequest request = RouteHelpers.RequiredBody<SecretImportRequest>(http);
            await RouteHelpers.JsonAsync(http, 200, SecretImporter.Parse(request)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            string tenantId = RouteHelpers.Tenant(http);
            Secret secret = await _Secrets.ReadAsync(tenantId, RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            secret.Referrers = await _Secrets.ReferrersAsync(tenantId, secret.Id, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, secret).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase http)
        {
            SecretRequest request = RouteHelpers.RequiredBody<SecretRequest>(http);
            Secret updated = await _Secrets.UpdateMetadataAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, request, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.SecretUpdated, "Secret", updated.Id, "Updated metadata of secret " + updated.Name + ".").ConfigureAwait(false);
            Publish("secret.updated", updated);
            await RouteHelpers.JsonAsync(http, 200, updated).ConfigureAwait(false);
        }

        private async Task ValuesAsync(HttpContextBase http)
        {
            SecretValueRequest request = RouteHelpers.RequiredBody<SecretValueRequest>(http);
            SecretValueUpdate result = await _Secrets.UpdateValuesAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, request, http.Token).ConfigureAwait(false);
            string changed = result.ChangedFields.Count == 0 ? "no fields" : "fields " + String.Join(", ", result.ChangedFields);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.SecretUpdated, "Secret", result.Secret.Id, "Updated values of secret " + result.Secret.Name + " (" + changed + ").").ConfigureAwait(false);
            Publish("secret.updated", result.Secret);
            await RouteHelpers.JsonAsync(http, 200, result.Secret).ConfigureAwait(false);
        }

        private async Task ReferrersAsync(HttpContextBase http)
        {
            List<SecretReferrer> referrers = await _Secrets.ReferrersAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, referrers).ConfigureAwait(false);
        }

        private async Task RevealAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            ctx.SuppressHistoryBodies = true;
            string tenantId = RouteHelpers.Tenant(http);
            string id = RouteHelpers.Param(http, "id")!;

            bool allowed = await _RevealIpLimiter.TryAcquireAsync(http.Request.Source.IpAddress, http.Token).ConfigureAwait(false);
            if (allowed) allowed = await _RevealPrincipalLimiter.TryAcquireAsync((ctx.PrincipalType + ":" + (ctx.PrincipalId ?? ctx.PrincipalName ?? "unknown")), http.Token).ConfigureAwait(false);
            if (!allowed)
            {
                http.Response.Headers["Retry-After"] = "60";
                await RouteHelpers.ErrorAsync(http, 429, "RateLimited", "Too many secret reveals. Try again in a minute.").ConfigureAwait(false);
                return;
            }

            // A secret that does not exist in the caller's tenant answers 404 before anything else is checked.
            await _Secrets.ReadAsync(tenantId, id, http.Token).ConfigureAwait(false);

            SecretRevealRequest request = RouteHelpers.Body<SecretRevealRequest>(http) ?? new SecretRevealRequest();
            string reason = (request.Reason ?? String.Empty).Trim();
            if (reason.Length == 0 || reason.Length > 1024)
            {
                Dictionary<string, string> fields = new Dictionary<string, string> { { "reason", reason.Length == 0 ? "A reason is required." : "The reason is longer than 1024 characters." } };
                Dictionary<string, string> codes = new Dictionary<string, string> { { "reason", reason.Length == 0 ? "Required" : "TooLong" } };
                throw new ValidationException("A reason between 1 and 1024 characters is required to reveal a secret.", fields, codes);
            }

            if (!RecentlyReauthenticated(ctx))
            {
                await RouteHelpers.ErrorAsync(http, 403, "ReauthenticationRequired", "Re-enter your password (POST /v1.0/token/reauthenticate) within the last " + _Server.Settings.Secrets.RevealReauthMinutes + " minutes before revealing a secret.").ConfigureAwait(false);
                return;
            }

            SecretReveal reveal = await _Secrets.RevealAsync(tenantId, id, http.Token).ConfigureAwait(false);
            AuditRecord record = new AuditRecord();
            record.EventType = AuditEventTypeEnum.SecretRevealed;
            record.TenantId = tenantId;
            record.RequestId = ctx.RequestId;
            record.AuthScheme = ctx.AuthScheme;
            record.PrincipalType = ctx.PrincipalType;
            record.PrincipalId = ctx.PrincipalId;
            record.PrincipalName = ctx.PrincipalName;
            record.SessionId = ctx.SessionId;
            record.SourceIp = ctx.SourceIp;
            record.ResourceType = "Secret";
            record.ResourceId = reveal.Secret.Id;
            record.Operation = OperationTypeEnum.Reveal.ToString();
            record.Method = http.Request.Method.ToString();
            record.Path = http.Request.Url.RawWithoutQuery;
            record.StatusCode = 200;
            record.Reason = reason;
            record.Description = "Revealed secret " + reveal.Secret.Name + " (fields " + String.Join(", ", reveal.Values.Keys.OrderBy(k => k, StringComparer.Ordinal)) + "). Reason: " + reason;
            await _Server.Audit.WriteAsync(record, http.Token).ConfigureAwait(false);

            SecretRevealResponse response = new SecretRevealResponse();
            response.SecretId = reveal.Secret.Id;
            response.Name = reveal.Secret.Name;
            response.Kind = reveal.Secret.Kind;
            response.Values = reveal.Values;
            response.RevealedUtc = reveal.Secret.LastRevealedUtc ?? DateTime.UtcNow;
            http.Response.Headers["Cache-Control"] = "no-store";
            await RouteHelpers.JsonAsync(http, 200, response).ConfigureAwait(false);
        }

        private bool RecentlyReauthenticated(RequestContext ctx)
        {
            // The administrator API key is presented on every request, so it counts as fresh. Credentials are machine
            // identities and never reveal; users must re-enter their password on the session first.
            if (ctx.AuthScheme == AuthSchemeEnum.AdminApiKey) return true;
            if (ctx.PrincipalType != PrincipalTypeEnum.User || ctx.Session == null || !ctx.Session.ReauthenticatedUtc.HasValue) return false;
            return DateTime.UtcNow - ctx.Session.ReauthenticatedUtc.Value <= TimeSpan.FromMinutes(_Server.Settings.Secrets.RevealReauthMinutes);
        }

        private async Task DeleteAsync(HttpContextBase http)
        {
            Secret deleted = await _Secrets.DeleteAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await ConfigurationSupport.AuditAsync(_Server, http, AuditEventTypeEnum.SecretDeleted, "Secret", deleted.Id, "Deleted secret " + deleted.Name + ".").ConfigureAwait(false);
            Publish("secret.deleted", deleted);
            await RouteHelpers.NoContentAsync(http).ConfigureAwait(false);
        }

        private void Publish(string type, Secret secret)
        {
            _Server.TryGet<EventHub>()?.Publish(type, secret.TenantId, new { id = secret.Id, name = secret.Name, kind = secret.Kind.ToString(), fields = secret.Fields });
        }

        #endregion
    }
}
