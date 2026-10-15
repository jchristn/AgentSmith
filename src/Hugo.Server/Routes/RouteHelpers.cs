namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Shared helpers for route handlers. Thread-safe.
    /// </summary>
    public static class RouteHelpers
    {
        #region Public-Methods

        /// <summary>
        /// Request context stored by the authentication hook.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Request context.</returns>
        public static RequestContext Context(HttpContextBase context)
        {
            if (context.Metadata is RequestContext requestContext) return requestContext;
            return RequestContext.Unauthenticated();
        }

        /// <summary>
        /// Tenant a request acts on. Tenant-scoped paths use the {tenantId} path parameter. Flat paths
        /// (/v1.0/api/resources and similar) take the tenant from the request context; a system administrator may pass
        /// ?tenantId= to act on another tenant (the authorization layer audits that bypass). A tenantId query parameter
        /// from anyone else is ignored, so a caller can never choose its tenant.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Tenant identifier.</returns>
        /// <exception cref="ArgumentException">Thrown when no tenant can be determined.</exception>
        public static string Tenant(HttpContextBase context)
        {
            string? tenantId = TargetTenant(context);
            if (String.IsNullOrEmpty(tenantId)) tenantId = Context(context).TenantId;
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentException("No tenant could be determined for this request.");
            return tenantId;
        }

        /// <summary>
        /// Tenant named explicitly by the request: the {tenantId} path parameter, or, for system administrators only, the
        /// tenantId query parameter. Null when the request names no tenant (the caller's own tenant applies).
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Tenant identifier or null.</returns>
        public static string? TargetTenant(HttpContextBase context)
        {
            string? tenantId = Param(context, "tenantId");
            if (!String.IsNullOrEmpty(tenantId)) return tenantId;
            if (Context(context).IsAdmin)
            {
                string? query = Query(context, "tenantId");
                if (!String.IsNullOrWhiteSpace(query)) return query.Trim();
            }
            return null;
        }

        /// <summary>
        /// Send a JSON response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="body">Body, or null.</param>
        /// <returns>Task.</returns>
        public static async Task JsonAsync(HttpContextBase context, int statusCode, object? body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.Send(Serializer.Serialize(body)).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a 204 response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Task.</returns>
        public static async Task NoContentAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        /// <summary>
        /// Send an error response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="statusCode">Status code.</param>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        /// <returns>Task.</returns>
        public static async Task ErrorAsync(HttpContextBase context, int statusCode, string code, string message)
        {
            ErrorResponse error = new ErrorResponse(ErrorName(statusCode), code, message);
            error.RequestId = Context(context).RequestId;
            await JsonAsync(context, statusCode, error).ConfigureAwait(false);
        }

        /// <summary>
        /// Send 503 for a database that has no free connections (DatabaseBusy) or cannot be reached (DatabaseUnavailable),
        /// with a Retry-After header. Retrying later is safe for idempotent requests and for creations that carry an
        /// Idempotency-Key.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="failure">Failure kind; None is reported as DatabaseUnavailable.</param>
        /// <param name="retryAfterSeconds">Seconds for the Retry-After header, minimum 1.</param>
        /// <returns>Task.</returns>
        public static async Task DatabaseFailureAsync(HttpContextBase context, DatabaseFailureEnum failure, int retryAfterSeconds)
        {
            context.Response.Headers["Retry-After"] = Math.Max(1, retryAfterSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (failure == DatabaseFailureEnum.Busy)
            {
                await ErrorAsync(context, 503, "DatabaseBusy", "The database has no free connections right now. Retry after the number of seconds in Retry-After.").ConfigureAwait(false);
            }
            else
            {
                await ErrorAsync(context, 503, "DatabaseUnavailable", "The database cannot be reached right now. Retry after the number of seconds in Retry-After.").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Send a 404 response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="what">What was not found.</param>
        /// <returns>Task.</returns>
        public static Task NotFoundAsync(HttpContextBase context, string what)
        {
            return ErrorAsync(context, 404, "NotFound", what + " not found.");
        }

        /// <summary>
        /// Send a 400 validation response with field errors.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="message">Message.</param>
        /// <param name="fields">Field errors.</param>
        /// <returns>Task.</returns>
        public static async Task ValidationAsync(HttpContextBase context, string message, Dictionary<string, string>? fields = null)
        {
            await ValidationAsync(context, message, fields, null).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a 400 validation response with field errors and their machine codes. Fields are reported as both
        /// fields and errors; every field gets a code in fieldCodes (Invalid when none was given).
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="message">Message.</param>
        /// <param name="fields">Field errors, or null.</param>
        /// <param name="codes">Machine codes keyed by field path, or null.</param>
        /// <returns>Task.</returns>
        public static async Task ValidationAsync(HttpContextBase context, string message, Dictionary<string, string>? fields, Dictionary<string, string>? codes)
        {
            await ValidationAsync(context, "ValidationFailed", message, fields, codes).ConfigureAwait(false);
        }

        /// <summary>
        /// Send a 400 validation response with a specific error code (for example ManifestInvalid), field errors, and
        /// their machine codes.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="code">Stable error code; null or empty means ValidationFailed.</param>
        /// <param name="message">Message.</param>
        /// <param name="fields">Field errors, or null.</param>
        /// <param name="codes">Machine codes keyed by field path, or null.</param>
        /// <returns>Task.</returns>
        public static async Task ValidationAsync(HttpContextBase context, string? code, string message, Dictionary<string, string>? fields, Dictionary<string, string>? codes)
        {
            ErrorResponse error = new ErrorResponse("BadRequest", String.IsNullOrEmpty(code) ? "ValidationFailed" : code, message);
            error.RequestId = Context(context).RequestId;
            if (fields != null && fields.Count > 0)
            {
                error.Fields = fields;
                error.Errors = fields;
                error.FieldCodes = new Dictionary<string, string>();
                foreach (string path in fields.Keys)
                {
                    error.FieldCodes[path] = codes != null && codes.TryGetValue(path, out string? fieldCode) && !String.IsNullOrEmpty(fieldCode) ? fieldCode : "Invalid";
                }
            }
            await JsonAsync(context, 400, error).ConfigureAwait(false);
        }

        /// <summary>
        /// Path parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static string? Param(HttpContextBase context, string key)
        {
            if (context.Request.Url.Parameters == null) return null;
            return context.Request.Url.Parameters[key];
        }

        /// <summary>
        /// Query parameter, decoded the way browsers and HTTP clients encode query strings
        /// (application/x-www-form-urlencoded): percent escapes are decoded and a plus sign is a space. Send a literal
        /// plus sign as %2B. The HTTP stack hands query values over still encoded, so every query parameter must be read
        /// through this method (or the typed variants below) rather than from the request directly.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Decoded value, or null when absent.</returns>
        public static string? Query(HttpContextBase context, string key)
        {
            string? raw = RawQuery(context, key);
            if (raw == null) return null;
            return DecodeQueryValue(raw);
        }

        /// <summary>
        /// Decode one query string value: a plus sign becomes a space and percent escapes are decoded as UTF-8.
        /// Malformed escapes are left as they are.
        /// </summary>
        /// <param name="raw">Raw, still encoded value.</param>
        /// <returns>Decoded value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when raw is null.</exception>
        public static string DecodeQueryValue(string raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.IndexOf('%') < 0 && raw.IndexOf('+') < 0) return raw;
            return WebUtility.UrlDecode(raw);
        }

        /// <summary>
        /// Integer query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static int? QueryInt(HttpContextBase context, string key)
        {
            string? value = Query(context, key);
            if (!String.IsNullOrEmpty(value) && Int32.TryParse(value, out int parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// Long query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static long? QueryLong(HttpContextBase context, string key)
        {
            string? value = Query(context, key);
            if (!String.IsNullOrEmpty(value) && Int64.TryParse(value, out long parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// Boolean query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static bool? QueryBool(HttpContextBase context, string key)
        {
            string? value = Query(context, key);
            if (!String.IsNullOrEmpty(value) && Boolean.TryParse(value, out bool parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// UTC timestamp query parameter.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value or null.</returns>
        public static DateTime? QueryDate(HttpContextBase context, string key)
        {
            // Percent escapes are decoded but a plus sign stays a plus sign, so a raw offset such as +02:00 still parses.
            string? value = RawQuery(context, key);
            if (String.IsNullOrEmpty(value)) return null;
            if (value.Contains('%')) value = Uri.UnescapeDataString(value);
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime parsed)) return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return null;
        }

        /// <summary>
        /// Enum query parameter.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Key.</param>
        /// <returns>Value, or null when the parameter is absent or empty.</returns>
        /// <exception cref="ValidationException">Thrown when the value is not one of the enum's names, so a mistyped filter
        /// is refused instead of silently matching everything.</exception>
        public static T? QueryEnum<T>(HttpContextBase context, string key) where T : struct, Enum
        {
            string? value = Query(context, key);
            if (String.IsNullOrEmpty(value)) return null;
            if (Enum.TryParse<T>(value, true, out T parsed) && Enum.IsDefined(parsed) && !Int64.TryParse(value, out long _)) return parsed;
            throw new ValidationException("Query parameter " + key + " must be one of: " + String.Join(", ", Enum.GetNames<T>()) + ".", new Dictionary<string, string> { { key, "Unknown value '" + value + "'." } });
        }

        /// <summary>
        /// Enumeration query from maxResults, skip, ordering, search, activeOnly, and includeBuiltIn.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Query.</returns>
        public static EnumerationQuery Enumeration(HttpContextBase context)
        {
            EnumerationQuery query = new EnumerationQuery();
            int? maxResults = QueryInt(context, "maxResults");
            if (maxResults.HasValue) query.MaxResults = maxResults.Value;
            int? skip = QueryInt(context, "skip");
            if (skip.HasValue) query.Skip = skip.Value;
            EnumerationOrderEnum? ordering = QueryEnum<EnumerationOrderEnum>(context, "ordering");
            if (ordering.HasValue) query.Ordering = ordering.Value;
            query.Search = Query(context, "search");
            query.ActiveOnly = QueryBool(context, "activeOnly") ?? false;
            query.IncludeBuiltIn = QueryBool(context, "includeBuiltIn") ?? true;
            return query;
        }

        /// <summary>
        /// Deserialize the request body.
        /// </summary>
        /// <typeparam name="T">Body type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <returns>Body, or null when empty.</returns>
        /// <exception cref="RequestBodyException">Thrown when the body is not valid JSON for the type.</exception>
        public static T? Body<T>(HttpContextBase context) where T : class
        {
            string body = context.Request.DataAsString;
            if (String.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return Serializer.Deserialize<T>(body);
            }
            catch (JsonException e)
            {
                throw new RequestBodyException("Request body is not valid JSON for " + typeof(T).Name + ": " + e.Message, e);
            }
        }

        /// <summary>
        /// Deserialize a required request body.
        /// </summary>
        /// <typeparam name="T">Body type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <returns>Body.</returns>
        /// <exception cref="RequestBodyException">Thrown when the body is missing or invalid.</exception>
        public static T RequiredBody<T>(HttpContextBase context) where T : class
        {
            T? body = Body<T>(context);
            if (body == null) throw new RequestBodyException("A JSON request body of type " + typeof(T).Name + " is required.");
            return body;
        }

        #endregion

        #region Private-Methods

        private static string? RawQuery(HttpContextBase context, string key)
        {
            if (context.Request.Query == null || context.Request.Query.Elements == null) return null;
            return context.Request.Query.Elements[key];
        }

        private static string ErrorName(int statusCode)
        {
            switch (statusCode)
            {
                case 400: return "BadRequest";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "NotFound";
                case 409: return "Conflict";
                case 429: return "TooManyRequests";
                case 502: return "BadGateway";
                case 503: return "ServiceUnavailable";
                default: return statusCode >= 500 ? "InternalServerError" : "Error";
            }
        }

        #endregion
    }
}
