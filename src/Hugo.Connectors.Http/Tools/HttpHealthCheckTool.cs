namespace Hugo.Connectors.Http.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>http_health_check</c>: probe a path and report status, latency, redirect chain, TLS certificate validity and
    /// expiry, and an optional body match. Always Read. A connection or TLS failure is reported as unhealthy, not as a
    /// tool error, so the caller can still see the certificate that failed. Thread-safe.
    /// </summary>
    public class HttpHealthCheckTool : HttpToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public static readonly string ToolName = "http_health_check";

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"path\":{\"type\":\"string\",\"description\":\"Path relative to the resource's base URL, inside the allowed prefixes, for example /health. Default /.\"},"
            + "\"method\":{\"type\":\"string\",\"enum\":[\"GET\",\"HEAD\"],\"description\":\"GET (default) or HEAD. HEAD cannot check bodyContains.\"},"
            + "\"expectStatus\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":599},\"description\":\"Status codes that count as healthy. Default: any 2xx or 3xx.\"},"
            + "\"bodyContains\":{\"type\":\"string\",\"description\":\"Text the response body must contain (case-sensitive), for example \\\"ok\\\".\"},"
            + "\"followRedirects\":{\"type\":\"boolean\",\"description\":\"Follow redirects within the resource's policy. Default: the resource's followRedirects setting.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public HttpHealthCheckTool(HttpConnectorOptions options) : base(BuildDescriptor(), options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, HttpResourceOptions resource, ToolArguments arguments, CancellationToken token)
        {
            string method = (arguments.GetString("method") ?? "GET").Trim().ToUpperInvariant();
            if (method != "GET" && method != "HEAD") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'method' must be GET or HEAD.");
            string? bodyContains = arguments.GetString("bodyContains");
            if (String.IsNullOrEmpty(bodyContains)) bodyContains = null;
            if (bodyContains != null && method == "HEAD") throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "bodyContains needs a GET request; HEAD returns no body.");

            List<int> expected = new List<int>();
            foreach (string s in arguments.GetStringList("expectStatus"))
            {
                if (!Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) || code < 100 || code > 599)
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "expectStatus entries must be integers from 100 to 599.");
                expected.Add(code);
            }

            HttpPathCheck check = HttpPathGuard.Check(arguments.GetString("path"), resource.AllowedPathPrefixes);
            if (!check.Allowed) throw new ConnectorException(check.Code, check.Reason);

            HttpOutgoingRequest request = new HttpOutgoingRequest
            {
                Method = method,
                EncodedPath = check.EncodedPath,
                FollowRedirects = arguments.GetBool("followRedirects", resource.FollowRedirects)
            };

            HttpAuthenticator auth = new HttpAuthenticator(context.Resource, resource, Options.TokenCache);
            HttpCertificateRecorder recorder = new HttpCertificateRecorder();
            HttpHealthPayload payload = new HttpHealthPayload
            {
                Method = method,
                BodyContains = bodyContains,
                Expected = expected.Any() ? String.Join(", ", expected.Select(c => c.ToString(CultureInfo.InvariantCulture))) : "2xx or 3xx"
            };

            using (HttpClient client = recorder.CreateClient(resource.VerifyTls))
            {
                HttpSender sender = new HttpSender(resource, auth, client, context.Resource.Logger);
                Uri url = sender.BuildUrl(request);
                payload.Url = url.AbsoluteUri;
                payload.FinalUrl = url.AbsoluteUri;
                DateTime started = DateTime.UtcNow;
                HttpExchange? exchange = null;
                try
                {
                    exchange = await sender.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (ConnectorException e) when (e.Code == ConnectorErrorCodes.ConnectionFailed)
                {
                    payload.ErrorCode = e.Code;
                    payload.Error = e.Message;
                    payload.LatencyMs = Math.Round((DateTime.UtcNow - started).TotalMilliseconds, 1);
                }

                Uri finalUrl = url;
                if (exchange != null)
                {
                    finalUrl = new Uri(exchange.FinalUrl);
                    payload.FinalUrl = exchange.FinalUrl;
                    payload.Status = exchange.StatusCode;
                    payload.LatencyMs = Math.Round(exchange.LatencyMs, 1);
                    payload.Redirects = exchange.Redirects;
                    payload.StatusOk = expected.Any() ? expected.Contains(exchange.StatusCode) : exchange.StatusCode >= 200 && exchange.StatusCode < 400;
                    if (bodyContains != null)
                    {
                        HttpBodyText.TryDecode(exchange.ContentType, exchange.Body, out string text);
                        payload.BodyMatched = text.Contains(bodyContains, StringComparison.Ordinal);
                    }
                }
                if (finalUrl.Scheme == Uri.UriSchemeHttps) payload.Tls = recorder.For(finalUrl);

                payload.Healthy = exchange != null && payload.StatusOk && (payload.BodyMatched ?? true);
                ToolResult result = ToolResult.FromText(OutputLimiter.Head(RenderText(payload, resource.VerifyTls), context.Limits.MaxBytes, context.Limits.MaxLines), HttpJson.Serialize(payload));
                result.Metrics["healthy"] = payload.Healthy ? 1 : 0;
                result.Metrics["httpStatus"] = payload.Status;
                if (payload.Tls != null) result.Metrics["tlsDaysRemaining"] = payload.Tls.DaysRemaining;
                return result;
            }
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            return new ToolDescriptor
            {
                Name = ToolName,
                Title = "HTTP health check",
                Description =
                    "Probe a path of this resource and report whether it is healthy: final status against the expected codes, total latency, the redirect chain, "
                    + "the TLS certificate (subject, issuer, expiry, days remaining, validity), and an optional bodyContains match. "
                    + "Use it for availability, latency, redirect, and certificate expiry questions before reaching for http_request. "
                    + "Connection and TLS failures are reported as unhealthy with the reason.",
                InputSchemaJson = InputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Text summary plus structured JSON {healthy, method, url, finalUrl, status, expected, statusOk, latencyMs, redirects[], tls{endpoint, subject, issuer, notBefore, notAfter, daysRemaining, sha256Thumbprint, valid, errors}, bodyContains, bodyMatched, errorCode, error}."
            };
        }

        private static string RenderText(HttpHealthPayload p, bool verifyTls)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Healthy: ").Append(p.Healthy ? "yes" : "no").Append('\n');
            sb.Append("URL: ").Append(p.Method).Append(' ').Append(p.Url).Append('\n');
            if (p.ErrorCode != null)
            {
                sb.Append("Error: ").Append(p.ErrorCode).Append(": ").Append(p.Error).Append('\n');
            }
            else
            {
                sb.Append("Status: ").Append(p.Status.ToString(CultureInfo.InvariantCulture)).Append(" (expected ").Append(p.Expected).Append(p.StatusOk ? ", ok" : ", NOT ok").Append(")\n");
                if (!String.Equals(p.FinalUrl, p.Url, StringComparison.Ordinal)) sb.Append("Final URL: ").Append(p.FinalUrl).Append('\n');
            }
            sb.Append("Latency: ").Append(p.LatencyMs.ToString("0.#", CultureInfo.InvariantCulture)).Append(" ms total\n");

            if (p.Redirects.Any())
            {
                sb.Append("Redirects:\n");
                foreach (HttpRedirectHop hop in p.Redirects)
                {
                    sb.Append("  ").Append(hop.Status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(hop.From).Append(" -> ").Append(hop.Location).Append(hop.Followed ? " (followed)" : String.Empty);
                    if (!String.IsNullOrEmpty(hop.Note)) sb.Append(" [").Append(hop.Note).Append(']');
                    sb.Append('\n');
                }
            }
            else
            {
                sb.Append("Redirects: none\n");
            }

            if (p.Tls != null)
            {
                sb.Append("TLS certificate: subject ").Append(p.Tls.Subject).Append(", issuer ").Append(p.Tls.Issuer).Append('\n');
                sb.Append("  Valid until ").Append(p.Tls.NotAfter.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                    .Append(" (").Append(p.Tls.DaysRemaining.ToString(CultureInfo.InvariantCulture)).Append(p.Tls.DaysRemaining < 0 ? " days, EXPIRED)" : " days remaining)").Append('\n');
                if (p.Tls.Valid) sb.Append("  Validation: passed\n");
                else sb.Append("  Validation: FAILED (").Append(p.Tls.Errors).Append(")").Append(verifyTls ? String.Empty : "; accepted only because verifyTls is false on this resource").Append('\n');
            }
            else if (p.Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("TLS certificate: not captured\n");
            }

            if (p.BodyContains != null)
                sb.Append("Body contains \"").Append(p.BodyContains).Append("\": ").Append(p.BodyMatched == true ? "yes" : "no").Append('\n');
            return sb.ToString();
        }

        #endregion
    }
}
