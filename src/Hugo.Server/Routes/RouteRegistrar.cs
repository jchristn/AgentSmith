namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Services;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using WatsonWebserver.Core.Routing;

    /// <summary>
    /// Registers routes on Watson with OpenAPI metadata, records each route's permission in the operation scope map,
    /// authorizes scoped routes before the handler runs (tenant from the {tenantId} parameter, resource from the
    /// resource parameter), and maps domain exceptions to HTTP responses. The tenant check, the authorization decision,
    /// and the handler all run inside one exception mapping, so every failure answers with the JSON error shape; a
    /// database that cannot be reached or has no free connections answers 503 with Retry-After (G-25, G-23).
    /// Thread-safe after registration.
    /// </summary>
    public class RouteRegistrar
    {
        #region Public-Members

        /// <summary>
        /// Operation scope map populated by this registrar.
        /// </summary>
        public OperationScopeMap Scopes
        {
            get
            {
                return _Scopes;
            }
        }

        /// <summary>
        /// Seconds sent in the Retry-After header of 503 DatabaseBusy and DatabaseUnavailable responses. Read on every
        /// such response. Default returns 2. Null is treated as the default.
        /// </summary>
        public Func<int> DatabaseRetryAfterSeconds
        {
            get
            {
                return _DatabaseRetryAfterSeconds;
            }
            set
            {
                _DatabaseRetryAfterSeconds = value ?? (() => 2);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _ParameterPattern = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        private readonly Webserver _Server;
        private readonly AuthorizationService _Authorization;
        private readonly DatabaseDriverBase _Database;
        private readonly OperationScopeMap _Scopes;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[Routes] ";
        private Func<int> _DatabaseRetryAfterSeconds = () => 2;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <param name="authorization">Authorization service.</param>
        /// <param name="database">Database, used to check that the tenant in a tenant-scoped path exists.</param>
        /// <param name="scopes">Operation scope map.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RouteRegistrar(Webserver server, AuthorizationService authorization, DatabaseDriverBase database, OperationScopeMap scopes, LoggingModule logging)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register an anonymous route.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path template.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="summary">OpenAPI summary.</param>
        /// <param name="tag">OpenAPI tag.</param>
        /// <param name="requestBody">Optional request body type.</param>
        /// <param name="response">Optional response type.</param>
        public void Anonymous(HttpMethod method, string path, Func<HttpContextBase, Task> handler, string summary, string tag, Type? requestBody = null, Type? response = null)
        {
            Record(method, path, null, null, RouteAuthorizationEnum.Anonymous, summary, tag);
            Func<HttpContextBase, Task> wrapped = context => InvokeAsync(context, handler);
            OpenApiRouteMetadata metadata = Metadata(method, path, summary, tag, requestBody, response, false);
            if (path.Contains('{')) _Server.Routes.PreAuthentication.Parameter.Add(method, path, wrapped, null, openApiMetadata: metadata);
            else _Server.Routes.PreAuthentication.Static.Add(method, path, wrapped, null, openApiMetadata: metadata);
        }

        /// <summary>
        /// Register an authenticated route authorized against a resource type and operation.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path template.</param>
        /// <param name="resourceType">Resource type.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="summary">OpenAPI summary.</param>
        /// <param name="tag">OpenAPI tag.</param>
        /// <param name="requestBody">Optional request body type.</param>
        /// <param name="response">Optional response type.</param>
        /// <param name="resourceParameter">Path parameter holding the target resource identifier. Default id.</param>
        /// <param name="queryParameters">Optional query parameters described in OpenAPI; when present, a 400 response is documented too.</param>
        public void Scoped(HttpMethod method, string path, ResourceTypeEnum resourceType, OperationTypeEnum operation, Func<HttpContextBase, Task> handler, string summary, string tag, Type? requestBody = null, Type? response = null, string resourceParameter = "id", List<OpenApiParameterMetadata>? queryParameters = null)
        {
            Record(method, path, resourceType, operation, RouteAuthorizationEnum.Scoped, summary, tag);
            Func<HttpContextBase, Task> authorized = async context =>
            {
                RequestContext ctx = RouteHelpers.Context(context);
                string? tenantId = RouteHelpers.TargetTenant(context);
                string? resourceId = RouteHelpers.Param(context, resourceParameter);
                AuthorizationDecision decision = await _Authorization.AuthorizeAsync(ctx, tenantId, resourceType, operation, resourceId, context.Token).ConfigureAwait(false);
                if (!decision.Permitted)
                {
                    await DenyAsync(context, ctx, decision, resourceType, operation, tenantId).ConfigureAwait(false);
                    return;
                }
                await handler(context).ConfigureAwait(false);
            };
            OpenApiRouteMetadata metadata = Metadata(method, path, summary, tag, requestBody, response, true);
            if (queryParameters != null && queryParameters.Count > 0)
            {
                foreach (OpenApiParameterMetadata parameter in queryParameters) metadata.WithParameter(parameter);
                metadata.WithResponse(400, OpenApiResponseMetadata.Json("Invalid query parameters.", OpenApiSchemaFactory.For(typeof(ErrorResponse))));
            }
            Add(method, path, authorized, metadata);
        }

        /// <summary>
        /// Register an authenticated route whose handler performs its own authorization.
        /// </summary>
        /// <param name="method">Method.</param>
        /// <param name="path">Path template.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="summary">OpenAPI summary.</param>
        /// <param name="tag">OpenAPI tag.</param>
        /// <param name="requestBody">Optional request body type.</param>
        /// <param name="response">Optional response type.</param>
        /// <param name="resourceType">Resource type the handler checks (documented in the scope map), or null.</param>
        /// <param name="operation">Operation the handler checks (documented in the scope map), or null.</param>
        public void Authenticated(HttpMethod method, string path, Func<HttpContextBase, Task> handler, string summary, string tag, Type? requestBody = null, Type? response = null, ResourceTypeEnum? resourceType = null, OperationTypeEnum? operation = null)
        {
            Record(method, path, resourceType, operation, RouteAuthorizationEnum.Handler, summary, tag);
            Add(method, path, handler, Metadata(method, path, summary, tag, requestBody, response, true));
        }

        /// <summary>
        /// Authorize inside a handler-authorized route and send the denial when not permitted.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="tenantId">Target tenant.</param>
        /// <param name="resourceType">Resource type.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <returns>True when permitted; false when a denial was sent.</returns>
        public async Task<bool> RequireAsync(HttpContextBase context, string? tenantId, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            AuthorizationDecision decision = await _Authorization.AuthorizeAsync(ctx, tenantId, resourceType, operation, resourceId, context.Token).ConfigureAwait(false);
            if (decision.Permitted) return true;
            await DenyAsync(context, ctx, decision, resourceType, operation, tenantId).ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// Routes registered on the Watson server without an operation scope (registered outside this registrar). The
        /// OpenAPI document routes are expected and excluded. A startup self-check logs these, and the route contract test
        /// fails when any exist. Unmapped authenticated routes fail closed: only system administrators reach them.
        /// </summary>
        /// <returns>Unmapped routes as "METHOD path".</returns>
        public List<string> UnmappedRoutes()
        {
            List<string> unmapped = new List<string>();
            foreach (KeyValuePair<HttpMethod, string> route in RegisteredRoutes())
            {
                if (IsDocumentationRoute(route.Value)) continue;
                // Watson stores static paths with a trailing slash; the scope map holds them as registered.
                string path = route.Value.Length > 1 ? route.Value.TrimEnd('/') : route.Value;
                if (_Scopes.Find(route.Key.ToString(), path) == null && _Scopes.Find(route.Key.ToString(), route.Value) == null) unmapped.Add(route.Key.ToString() + " " + path);
            }
            return unmapped.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Whether an authenticated request may reach its route: true when the matching post-authentication route has an
        /// operation scope, or when no route matches (Watson answers 404). False for a route registered outside this
        /// registrar, which fails closed to system administrators.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path without query.</param>
        /// <returns>True when the route is mapped or unknown.</returns>
        public bool IsMappedOrUnknown(HttpMethod method, string? path)
        {
            if (String.IsNullOrEmpty(path)) return true;
            Func<HttpContextBase, Task>? staticHandler = _Server.Routes.PostAuthentication.Static.Match(method, path, out StaticRoute? staticRoute);
            if (staticHandler != null && staticRoute != null)
            {
                string template = staticRoute.Path.Length > 1 ? staticRoute.Path.TrimEnd('/') : staticRoute.Path;
                return _Scopes.Find(method.ToString(), template) != null || _Scopes.Find(method.ToString(), staticRoute.Path) != null;
            }
            Func<HttpContextBase, Task>? parameterHandler = _Server.Routes.PostAuthentication.Parameter.Match(method, path, out System.Collections.Specialized.NameValueCollection _, out ParameterRoute? parameterRoute);
            if (parameterHandler != null && parameterRoute != null)
            {
                return _Scopes.Find(method.ToString(), parameterRoute.Path) != null;
            }
            return true;
        }

        /// <summary>
        /// Every route registered on the Watson server (pre- and post-authentication, static and parameter).
        /// </summary>
        /// <returns>Method and path pairs.</returns>
        public List<KeyValuePair<HttpMethod, string>> RegisteredRoutes()
        {
            List<KeyValuePair<HttpMethod, string>> routes = new List<KeyValuePair<HttpMethod, string>>();
            foreach (StaticRoute route in _Server.Routes.PreAuthentication.Static.GetAll()) routes.Add(new KeyValuePair<HttpMethod, string>(route.Method, route.Path));
            foreach (ParameterRoute route in _Server.Routes.PreAuthentication.Parameter.GetAll()) routes.Add(new KeyValuePair<HttpMethod, string>(route.Method, route.Path));
            foreach (StaticRoute route in _Server.Routes.PostAuthentication.Static.GetAll()) routes.Add(new KeyValuePair<HttpMethod, string>(route.Method, route.Path));
            foreach (ParameterRoute route in _Server.Routes.PostAuthentication.Parameter.GetAll()) routes.Add(new KeyValuePair<HttpMethod, string>(route.Method, route.Path));
            return routes;
        }

        /// <summary>
        /// Whether a path is one of the OpenAPI document routes (anonymous by design).
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True for /openapi.json and /swagger.</returns>
        public static bool IsDocumentationRoute(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            string normalized = path.TrimEnd('/');
            return normalized.Equals("/openapi.json", StringComparison.OrdinalIgnoreCase) || normalized.Equals("/swagger", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        private void Add(HttpMethod method, string path, Func<HttpContextBase, Task> handler, OpenApiRouteMetadata metadata)
        {
            // Every authenticated route under /tenants/{tenantId} checks here, once, that the tenant exists. A user whose
            // password is still the seeded or reset default is only prompted to change it (MustChangePassword on the token
            // and identity responses); no route is blocked (D-048).
            Func<HttpContextBase, Task> inner = handler;
            if (path.Contains("/tenants/{tenantId}", StringComparison.Ordinal) || path.StartsWith("/v1.0/api/", StringComparison.Ordinal))
            {
                Func<HttpContextBase, Task> scoped = inner;
                inner = async context =>
                {
                    if (await TenantMissingAsync(context).ConfigureAwait(false)) return;
                    await scoped(context).ConfigureAwait(false);
                };
            }
            // The tenant check and the authorization decision read the database, so they run inside the same exception
            // mapping as the handler (G-25).
            Func<HttpContextBase, Task> wrapped = context => InvokeAsync(context, inner);
            if (path.Contains('{')) _Server.Routes.PostAuthentication.Parameter.Add(method, path, wrapped, null, openApiMetadata: metadata);
            else _Server.Routes.PostAuthentication.Static.Add(method, path, wrapped, null, openApiMetadata: metadata);
        }

        private void Record(HttpMethod method, string path, ResourceTypeEnum? resourceType, OperationTypeEnum? operation, RouteAuthorizationEnum authorization, string summary, string tag)
        {
            OperationScope scope = new OperationScope();
            scope.Method = method.ToString();
            scope.Path = path;
            scope.ResourceType = resourceType;
            scope.Operation = operation;
            scope.Authorization = authorization;
            scope.Summary = summary;
            scope.Tag = tag;
            _Scopes.Add(scope);
        }

        private static OpenApiRouteMetadata Metadata(HttpMethod method, string path, string summary, string tag, Type? requestBody, Type? response, bool secured)
        {
            OpenApiRouteMetadata metadata = OpenApiRouteMetadata.Create(summary, tag);
            foreach (Match match in _ParameterPattern.Matches(path))
            {
                metadata.WithParameter(OpenApiParameterMetadata.Path(match.Groups[1].Value, match.Groups[1].Value + " path parameter.", OpenApiSchemaMetadata.String()));
            }
            if (requestBody != null) metadata.WithRequestBody(OpenApiRequestBodyMetadata.Json(OpenApiSchemaFactory.For(requestBody), requestBody.Name, true));
            if (response != null) metadata.WithResponse(200, OpenApiResponseMetadata.Json("Success.", OpenApiSchemaFactory.For(response)));
            else if (method == HttpMethod.DELETE) metadata.WithResponse(204, OpenApiResponseMetadata.NoContent());
            if (secured)
            {
                metadata.WithResponse(401, OpenApiResponseMetadata.Unauthorized());
                metadata.WithResponse(403, OpenApiResponseMetadata.Forbidden());
            }
            return metadata;
        }

        private async Task<bool> TenantMissingAsync(HttpContextBase context)
        {
            // Only system administrators can address a tenant other than their own; everyone else already gets the
            // authorization layer's 404 for another tenant, which must not reveal whether that tenant exists.
            RequestContext ctx = RouteHelpers.Context(context);
            string? tenantId = RouteHelpers.TargetTenant(context);
            if (!ctx.IsAdmin || String.IsNullOrEmpty(tenantId)) return false;
            if (await _Database.Tenants.ExistsAsync(tenantId, context.Token).ConfigureAwait(false)) return false;
            await RouteHelpers.ErrorAsync(context, 404, "TenantNotFound", "Tenant " + tenantId + " not found.").ConfigureAwait(false);
            return true;
        }

        private async Task DenyAsync(HttpContextBase context, RequestContext ctx, AuthorizationDecision decision, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? tenantId)
        {
            int status = decision.Result == Hugo.Core.Enums.AuthorizationResultEnum.NotFound ? 404 : 403;
            ErrorResponse error = new ErrorResponse(status == 404 ? "NotFound" : "Forbidden", status == 404 ? "NotFound" : "AuthorizationDenied", status == 404 ? "Resource not found." : decision.Reason);
            error.RequestId = ctx.RequestId;
            if (status == 403)
            {
                error.RequiredPermission = operation.ToString();
                error.ResourceType = resourceType.ToString();
                error.TenantId = tenantId;
            }
            await RouteHelpers.JsonAsync(context, status, error).ConfigureAwait(false);
        }

        private async Task InvokeAsync(HttpContextBase context, Func<HttpContextBase, Task> handler)
        {
            RequestTelemetry.CaptureSpan(context);
            try
            {
                await handler(context).ConfigureAwait(false);
            }
            catch (RequestBodyException e)
            {
                await RouteHelpers.ErrorAsync(context, 400, "InvalidBody", e.Message).ConfigureAwait(false);
            }
            catch (ValidationException e)
            {
                await RouteHelpers.ValidationAsync(context, e.Code, e.Message, e.Fields.Count > 0 ? e.Fields : null, e.Codes).ConfigureAwait(false);
            }
            catch (NotFoundException e)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", e.Message).ConfigureAwait(false);
            }
            catch (Hugo.Core.Security.MasterKeyException e)
            {
                await RouteHelpers.ErrorAsync(context, 503, "MasterKeyUnavailable", e.Message).ConfigureAwait(false);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                await RouteHelpers.ErrorAsync(context, 500, "DecryptionFailed", "A stored value could not be decrypted with the configured master keys.").ConfigureAwait(false);
            }
            catch (ConflictException e)
            {
                await RouteHelpers.ErrorAsync(context, 409, e.Code, e.Message).ConfigureAwait(false);
            }
            catch (ForbiddenException e)
            {
                await RouteHelpers.ErrorAsync(context, 403, e.Code, e.Message).ConfigureAwait(false);
            }
            catch (ArgumentException e)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", e.Message).ConfigureAwait(false);
            }
            catch (JsonException e)
            {
                await RouteHelpers.ErrorAsync(context, 400, "InvalidJson", e.Message).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!context.Response.ResponseSent) await RouteHelpers.ErrorAsync(context, 503, "Cancelled", "The request was cancelled.").ConfigureAwait(false);
            }
            catch (Exception e) when (DatabaseFailures.Classify(e) != DatabaseFailureEnum.None)
            {
                DatabaseFailureEnum failure = DatabaseFailures.Classify(e);
                _Logging.Warn(_Header + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " failed: database " + failure.ToString().ToLowerInvariant() + ": " + e.Message + " "
                    + HugoTracing.LogFields("route", RequestTelemetry.RouteTemplate(context), "tenant", RequestTelemetry.Tenant(context), "trace", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
                if (!context.Response.ResponseSent) await RouteHelpers.DatabaseFailureAsync(context, failure, _DatabaseRetryAfterSeconds()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " failed: " + SecretScrubber.Shared.Scrub(e.ToString()) + " "
                    + HugoTracing.LogFields("route", RequestTelemetry.RouteTemplate(context), "tenant", RequestTelemetry.Tenant(context), "trace", System.Diagnostics.Activity.Current?.TraceId.ToHexString()));
                if (!context.Response.ResponseSent) await RouteHelpers.ErrorAsync(context, 500, "InternalError", "An internal error occurred. See server logs with the request identifier.").ConfigureAwait(false);
            }
        }

        #endregion
    }
}
