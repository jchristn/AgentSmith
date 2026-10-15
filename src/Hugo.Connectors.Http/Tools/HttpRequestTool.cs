namespace Hugo.Connectors.Http.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>http_request</c>: one request to a path under the resource's base URL. Classified per call from the method,
    /// the path, and the resource's overrides (see <see cref="HttpRequestClassifier"/>). An HTTP error status is a
    /// successful call that reports the status; connection, TLS, and timeout failures are tool errors. Thread-safe.
    /// </summary>
    public class HttpRequestTool : HttpToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "http_request";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"method\":{\"type\":\"string\",\"description\":\"HTTP method. Common values: GET, HEAD, OPTIONS, POST, PUT, PATCH, DELETE. Must be in the resource's allowedMethods. Default GET.\",\"examples\":[\"GET\",\"POST\",\"PUT\",\"PATCH\",\"DELETE\",\"HEAD\",\"OPTIONS\"]},"
            + "\"path\":{\"type\":\"string\",\"description\":\"Path relative to the resource's base URL, for example /orders/42. Must stay inside the allowed path prefixes. No absolute URLs, no '..', no query string (use 'query'). Default /.\"},"
            + "\"query\":{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"},\"description\":\"Query parameters as name to value. Values are URL-encoded for you.\"},"
            + "\"headers\":{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"},\"description\":\"Extra request headers. Authorization, cookies, the API key header, Host, Content-Length, Transfer-Encoding, and Connection cannot be set; the resource's authentication is applied automatically.\"},"
            + "\"body\":{\"description\":\"Request body. A JSON object or array is sent as application/json; a string is sent as-is (set contentType). Not allowed with HEAD. Never put credentials in a body.\",\"type\":[\"string\",\"object\",\"array\"]},"
            + "\"contentType\":{\"type\":\"string\",\"description\":\"Content-Type of the body. Defaults to application/json for objects and arrays and text/plain; charset=utf-8 for strings.\"}"
            + "}}";

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _RedactedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Set-Cookie", "Set-Cookie2" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public HttpRequestTool(HttpConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            HttpResourceOptions resource = HttpResourceOptions.FromSettings(context.Resource.Settings);
            return HttpRequestClassifier.Classify(arguments.GetString("method"), arguments.GetString("path"), resource, Target(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, HttpResourceOptions resource, ToolArguments arguments, CancellationToken token)
        {
            HttpOutgoingRequest request = BuildRequest(resource, arguments);
            HttpAuthenticator auth = new HttpAuthenticator(context.Resource, resource, Options.TokenCache);
            HttpSender sender = new HttpSender(resource, auth, null, context.Resource.Logger);
            HttpExchange exchange = await sender.SendAsync(request, token).ConfigureAwait(false);
            return Render(exchange, context.Limits);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "Send HTTP request",
                Description =
                    "Send one HTTP request to a path under this resource's base URL and return the status, headers, and body (bounded). "
                    + "GET, HEAD, and OPTIONS are Read; POST, PUT, and PATCH are Write; DELETE and unusual methods are Destructive, unless the resource overrides a path. "
                    + "Only paths inside the allowed prefixes work. Authentication is applied automatically; never send credentials yourself. "
                    + "An HTTP error status (404, 500) is reported, not raised, so read the status before the body.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                ClassifiedPerCall = true,
                OutputDescription = "Status line, redirects, headers (Set-Cookie redacted), and the body as text (binary bodies summarized), plus structured JSON {method, url, status, reason, headers, contentType, bodyBytes, bodyTruncated, bodyIsText, redirects, authRetried, retryAfterSeconds, latencyMs}."
            };
        }

        private static HttpOutgoingRequest BuildRequest(HttpResourceOptions resource, ToolArguments arguments)
        {
            string method = (arguments.GetString("method") ?? "GET").Trim().ToUpperInvariant();
            if (method.Length == 0) method = "GET";
            if (!HttpRequestClassifier.IsMethodToken(method))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'method' is not a valid HTTP method.");
            if (!resource.AllowedMethods.Contains(method))
                throw new ConnectorException(ConnectorErrorCodes.PermissionDenied, "Method " + method + " is not allowed on this resource. Allowed: " + String.Join(", ", resource.AllowedMethods.OrderBy(m => m, StringComparer.Ordinal)) + ".");

            HttpPathCheck check = HttpPathGuard.Check(arguments.GetString("path"), resource.AllowedPathPrefixes);
            if (!check.Allowed) throw new ConnectorException(check.Code, check.Reason);

            HttpOutgoingRequest request = new HttpOutgoingRequest
            {
                Method = method,
                EncodedPath = check.EncodedPath,
                FollowRedirects = resource.FollowRedirects
            };

            foreach (KeyValuePair<string, string> q in arguments.GetStringMap("query"))
            {
                if (String.IsNullOrEmpty(q.Key)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Query parameter names must not be empty.");
                request.Query.Add(new KeyValuePair<string, string>(q.Key, q.Value));
            }

            HashSet<string> reserved = HttpAuthenticator.ReservedHeaderNames(resource);
            string? contentType = arguments.GetString("contentType");
            foreach (KeyValuePair<string, string> h in arguments.GetStringMap("headers"))
            {
                if (!HttpRequestClassifier.IsToken(h.Key)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Header name '" + h.Key + "' is not valid.");
                if (reserved.Contains(h.Key))
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Header '" + h.Key + "' cannot be set by a call. The resource's authentication is applied automatically; never send credentials yourself.");
                if (HttpSender.IsTransportHeader(h.Key)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Header '" + h.Key + "' is controlled by the transport and cannot be set.");
                if (h.Value.Any(c => c == '\r' || c == '\n' || c == '\0')) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Header '" + h.Key + "' has a value with line breaks.");
                if (String.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (String.IsNullOrEmpty(contentType)) contentType = h.Value;
                    continue;
                }
                request.Headers.Add(new KeyValuePair<string, string>(h.Key, h.Value));
            }

            string? raw = arguments.GetRawJson("body");
            if (raw != null)
            {
                if (method == "HEAD") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "A HEAD request cannot have a body.");
                bool isString = raw.TrimStart().StartsWith('"');
                string text = isString ? (arguments.GetString("body") ?? String.Empty) : raw;
                request.Body = Encoding.UTF8.GetBytes(text);
                if (request.Body.Length > resource.MaxRequestBytes)
                    throw new ConnectorException(ConnectorErrorCodes.TooLarge, "The body is " + request.Body.Length.ToString(CultureInfo.InvariantCulture) + " bytes; this resource accepts at most " + resource.MaxRequestBytes.ToString(CultureInfo.InvariantCulture) + " (maxRequestBytes).");
                if (String.IsNullOrEmpty(contentType)) contentType = isString ? "text/plain; charset=utf-8" : "application/json";
            }

            if (contentType != null && contentType.Any(c => c == '\r' || c == '\n' || c == '\0'))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'contentType' has line breaks.");
            request.ContentType = contentType;
            return request;
        }

        private ToolResult Render(HttpExchange exchange, OutputLimits limits)
        {
            ToolResult result = new ToolResult();
            HttpRequestPayload payload = new HttpRequestPayload
            {
                Method = exchange.Method,
                Url = exchange.FinalUrl,
                Status = exchange.StatusCode,
                Reason = exchange.ReasonPhrase,
                ContentType = exchange.ContentType,
                BodyBytes = exchange.Body.Length,
                BodyTruncated = exchange.BodyTruncated,
                Redirects = exchange.Redirects,
                AuthRetried = exchange.AuthRetried,
                RetryAfterSeconds = exchange.RetryAfterSeconds,
                LatencyMs = Math.Round(exchange.LatencyMs, 1)
            };

            StringBuilder sb = new StringBuilder();
            sb.Append("HTTP ").Append(exchange.StatusCode.ToString(CultureInfo.InvariantCulture));
            if (!String.IsNullOrEmpty(exchange.ReasonPhrase)) sb.Append(' ').Append(exchange.ReasonPhrase);
            sb.Append(" (").Append(exchange.Method).Append(' ').Append(exchange.FinalUrl).Append(", ")
                .Append(exchange.LatencyMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms)\n");
            if (exchange.AuthRetried) sb.Append("Note: the OAuth2 token was refreshed after a 401 and the request was retried once.\n");

            if (exchange.Redirects.Any())
            {
                sb.Append("Redirects:\n");
                foreach (HttpRedirectHop hop in exchange.Redirects)
                {
                    sb.Append("  ").Append(hop.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(hop.From).Append(" -> ").Append(hop.Location);
                    sb.Append(hop.Followed ? " (followed)" : String.Empty);
                    if (!String.IsNullOrEmpty(hop.Note)) sb.Append(" [").Append(hop.Note).Append(']');
                    sb.Append('\n');
                }
            }

            sb.Append("Headers:\n");
            foreach (KeyValuePair<string, string> h in exchange.Headers)
            {
                string value = _RedactedHeaders.Contains(h.Key) ? "[redacted]" : Shorten(h.Value, Options.MaxHeaderValueChars);
                sb.Append("  ").Append(h.Key).Append(": ").Append(value).Append('\n');
                payload.Headers[h.Key] = payload.Headers.TryGetValue(h.Key, out string? existing) && !_RedactedHeaders.Contains(h.Key) ? existing + ", " + value : value;
            }

            if (exchange.Method == "HEAD")
            {
                sb.Append("Body: none (HEAD request)\n");
            }
            else if (exchange.Body.Length == 0)
            {
                sb.Append("Body: empty\n");
            }
            else
            {
                string size = exchange.Body.Length.ToString(CultureInfo.InvariantCulture) + " bytes" + (exchange.BodyTruncated ? ", cut at the resource's maxResponseBytes" : String.Empty);
                if (HttpBodyText.TryDecode(exchange.ContentType, exchange.Body, out string text))
                {
                    sb.Append("Body (").Append(size).Append(String.IsNullOrEmpty(exchange.ContentType) ? String.Empty : ", " + exchange.ContentType).Append("):\n");
                    sb.Append(text);
                    if (!text.EndsWith('\n')) sb.Append('\n');
                }
                else
                {
                    payload.BodyIsText = false;
                    sb.Append("Body: binary, ").Append(size).Append(", ").Append(exchange.ContentType ?? "no content type").Append(". Not shown.");
                    int preview = Math.Min(Options.BinaryPreviewBytes, exchange.Body.Length);
                    if (preview > 0) sb.Append(" First ").Append(preview.ToString(CultureInfo.InvariantCulture)).Append(" bytes (base64): ").Append(Convert.ToBase64String(exchange.Body, 0, preview));
                    sb.Append('\n');
                }
            }

            if (exchange.BodyTruncated)
            {
                result.Truncated = true;
                result.OriginalBytes = exchange.ContentLength ?? exchange.Body.Length;
            }

            result.Text = OutputLimiter.Head(sb.ToString(), limits.MaxBytes, limits.MaxLines, result);
            result.StructuredJson = HttpJson.Serialize(payload);
            result.MimeType = "text/plain";
            result.RetryAfterSeconds = exchange.RetryAfterSeconds;
            result.Metrics["httpStatus"] = exchange.StatusCode;
            result.Metrics["bodyBytes"] = exchange.Body.Length;
            result.Metrics["redirects"] = exchange.Redirects.Count(r => r.Followed);
            return result;
        }

        private static string Shorten(string value, int max)
        {
            string v = (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return v.Length > max ? v.Substring(0, max - 3) + "..." : v;
        }

        #endregion
    }
}
