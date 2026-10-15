namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Http.Tools;

    /// <summary>
    /// Generic HTTP API connector (type <c>http</c>, family <c>http</c>). One resource is a base URL with its
    /// authentication, method and path allow lists, and effect overrides. <c>http_request</c> is classified per call;
    /// <c>http_health_check</c> is Read. Thread-safe.
    /// </summary>
    public class HttpConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "http";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "http";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Options shared with the tools. Never null.
        /// </summary>
        public HttpConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with default options.
        /// </summary>
        public HttpConnector() : this(new HttpConnectorOptions())
        {
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public HttpConnector(HttpConnectorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new HttpRequestTool(Options),
                new HttpHealthCheckTool(Options)
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();
            ResourceSettings s = resource.Settings;
            HttpResourceOptions parsed = HttpResourceOptions.FromSettings(s);

            if (!HttpResourceOptions.TryParseBaseUrl(s.GetString(HttpSettingKeys.BaseUrl), out Uri? _, out string baseError))
                result.Add(HttpSettingKeys.BaseUrl, String.IsNullOrWhiteSpace(s.GetString(HttpSettingKeys.BaseUrl)) ? "required" : "invalid_format", baseError);

            string apiKeyHeader = s.GetString(HttpSettingKeys.ApiKeyHeaderName, "X-API-Key") ?? "X-API-Key";
            if (!HttpRequestClassifier.IsToken(apiKeyHeader.Trim()))
                result.Add(HttpSettingKeys.ApiKeyHeaderName, "invalid_format", "apiKeyHeaderName must be a valid header name such as X-API-Key.");

            HashSet<string> reserved = HttpAuthenticator.ReservedHeaderNames(parsed);
            foreach (string line in s.GetStringList(HttpSettingKeys.DefaultHeaders))
            {
                if (!HttpResourceOptions.TryParseHeaderLine(line, out string name, out string _, out string headerError))
                {
                    result.Add(HttpSettingKeys.DefaultHeaders, "invalid_format", headerError);
                    break;
                }
                if (reserved.Contains(name) || HttpSender.IsTransportHeader(name))
                {
                    result.Add(HttpSettingKeys.DefaultHeaders, "invalid_value", "Header '" + name + "' cannot be a default header. Credentials belong in a secret slot with the matching authType.");
                    break;
                }
            }

            string authText = s.GetString(HttpSettingKeys.AuthType, "None") ?? "None";
            if (String.IsNullOrWhiteSpace(authText) || !Enum.TryParse<HttpAuthTypeEnum>(authText, true, out HttpAuthTypeEnum auth) || !Enum.IsDefined(typeof(HttpAuthTypeEnum), auth) || Char.IsDigit(authText.Trim()[0]))
            {
                result.Add(HttpSettingKeys.AuthType, "invalid_value", "authType must be None, Basic, Bearer, ApiKeyHeader, or OAuth2ClientCredentials.");
            }
            else
            {
                ValidateAuth(resource, auth, result);
            }

            foreach (string method in s.GetStringList(HttpSettingKeys.AllowedMethods).SelectMany(l => l.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)))
            {
                if (!HttpRequestClassifier.IsMethodToken(method))
                {
                    result.Add(HttpSettingKeys.AllowedMethods, "invalid_format", "'" + method + "' is not an HTTP method.");
                    break;
                }
            }

            foreach (string prefix in s.GetStringList(HttpSettingKeys.AllowedPathPrefixes))
            {
                if (!HttpPathGuard.IsValidPrefix(prefix, out string prefixError))
                {
                    result.Add(HttpSettingKeys.AllowedPathPrefixes, "invalid_format", prefixError);
                    break;
                }
            }

            foreach (string line in s.GetStringList(HttpSettingKeys.MethodEffectOverrides))
            {
                if (!HttpEffectOverride.TryParse(line, out HttpEffectOverride? _, out string overrideError))
                {
                    result.Add(HttpSettingKeys.MethodEffectOverrides, "invalid_format", "'" + line + "': " + overrideError);
                    break;
                }
            }

            if (parsed.HealthPath != null)
            {
                HttpPathCheck healthCheck = HttpPathGuard.Check(parsed.HealthPath, parsed.AllowedPathPrefixes);
                if (!healthCheck.Allowed) result.Add(HttpSettingKeys.HealthPath, "invalid_value", "healthPath must be a path under one of the allowed path prefixes, for example /health.");
            }

            ValidateRange(s, result, HttpSettingKeys.TimeoutSeconds, 1, 600);
            ValidateRange(s, result, HttpSettingKeys.MaxResponseBytes, 1024, 64 * 1024 * 1024);
            ValidateRange(s, result, HttpSettingKeys.MaxRequestBytes, 1, 64 * 1024 * 1024);
            ValidateRange(s, result, HttpSettingKeys.MaxRedirects, 0, 20);
            ValidateBool(s, result, HttpSettingKeys.FollowRedirects);
            ValidateBool(s, result, HttpSettingKeys.VerifyTls);
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            HttpResourceOptions options = HttpResourceOptions.FromSettings(resource.Settings);
            TimeSpan limit = options.Timeout < Options.TestConnectionTimeout ? options.Timeout : Options.TestConnectionTimeout;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(limit);
                try
                {
                    HttpAuthenticator auth = new HttpAuthenticator(resource, options, Options.TokenCache);
                    HttpSender sender = new HttpSender(options, auth, null, resource.Logger);
                    // With healthPath set, the test is a health check: only a 2xx or 3xx answer passes. Without it, the
                    // test probes the first allowed prefix and passes whenever the server answers (reachability), and a
                    // 4xx answer is called out in the message and details instead of being reported as healthy.
                    bool healthChecked = options.HealthPath != null;
                    string probePath = options.HealthPath ?? options.AllowedPathPrefixes.FirstOrDefault() ?? "/";
                    HttpPathCheck check = HttpPathGuard.Check(probePath, options.AllowedPathPrefixes);
                    HttpOutgoingRequest request = new HttpOutgoingRequest
                    {
                        Method = "GET",
                        EncodedPath = check.Allowed ? check.EncodedPath : "/",
                        FollowRedirects = options.FollowRedirects
                    };
                    HttpExchange exchange = await sender.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    double ms = sw.Elapsed.TotalMilliseconds;
                    string statusText = "HTTP " + exchange.StatusCode.ToString(CultureInfo.InvariantCulture);
                    ConnectorHealthResult health;
                    if (exchange.StatusCode == 401 || exchange.StatusCode == 403)
                        health = ConnectorHealthResult.Fail(ConnectorErrorCodes.AuthFailed, exchange.FinalUrl + " answered " + statusText + ". Check the authType and the bound secret.", ms);
                    else if (exchange.StatusCode >= 500)
                        health = ConnectorHealthResult.Fail(ConnectorErrorCodes.UpstreamError, exchange.FinalUrl + " answered " + statusText + ".", ms);
                    else if (exchange.StatusCode >= 400 && healthChecked)
                        health = ConnectorHealthResult.Fail(ConnectorErrorCodes.UpstreamError, "The health path " + exchange.FinalUrl + " answered " + statusText + "; healthPath must answer 2xx or 3xx.", ms);
                    else if (exchange.StatusCode >= 400)
                        health = ConnectorHealthResult.Ok("The server is reachable, but " + exchange.FinalUrl + " answered " + statusText + ", so this is not a health check. Set healthPath to a path that answers 2xx (for example /health) to check health.", ms);
                    else
                        health = ConnectorHealthResult.Ok((healthChecked ? "Healthy: " : "Reached ") + exchange.FinalUrl + " (" + statusText + ").", ms);
                    health.Details["status"] = exchange.StatusCode.ToString(CultureInfo.InvariantCulture);
                    health.Details["url"] = exchange.FinalUrl;
                    health.Details["authType"] = options.AuthType.ToString();
                    health.Details["healthChecked"] = healthChecked ? "true" : "false";
                    health.Details["reachableOnly"] = !healthChecked && exchange.StatusCode >= 400 ? "true" : "false";
                    return health;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The server did not respond within " + limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void ValidateAuth(ConnectorResourceContext resource, HttpAuthTypeEnum auth, ConnectorValidationResult result)
        {
            SecretAccessor secrets = resource.Secrets;
            switch (auth)
            {
                case HttpAuthTypeEnum.Basic:
                    if (String.IsNullOrEmpty(secrets.Get(HttpSettingKeys.BasicAuthSlot, HttpSettingKeys.UsernameField)) || secrets.Get(HttpSettingKeys.BasicAuthSlot, HttpSettingKeys.PasswordField) == null)
                        result.Add(HttpSettingKeys.BasicAuthSlot, "required", "authType is Basic, so bind a Password secret with a username to the 'basicAuth' slot.");
                    break;
                case HttpAuthTypeEnum.Bearer:
                    if (String.IsNullOrEmpty(secrets.Get(HttpSettingKeys.BearerTokenSlot, HttpSettingKeys.TokenField)))
                        result.Add(HttpSettingKeys.BearerTokenSlot, "required", "authType is Bearer, so bind a BearerToken (or ApiToken) secret to the 'bearerToken' slot.");
                    break;
                case HttpAuthTypeEnum.ApiKeyHeader:
                    if (String.IsNullOrEmpty(secrets.Get(HttpSettingKeys.ApiKeySlot, HttpSettingKeys.TokenField)))
                        result.Add(HttpSettingKeys.ApiKeySlot, "required", "authType is ApiKeyHeader, so bind an ApiToken secret to the 'apiKey' slot.");
                    break;
                case HttpAuthTypeEnum.OAuth2ClientCredentials:
                    string? tokenUrl = resource.Settings.GetString(HttpSettingKeys.TokenUrl);
                    if (String.IsNullOrWhiteSpace(tokenUrl))
                        result.Add(HttpSettingKeys.TokenUrl, "required", "authType is OAuth2ClientCredentials, so tokenUrl is required.");
                    else if (!Uri.TryCreate(tokenUrl.Trim(), UriKind.Absolute, out Uri? tokenUri) || (tokenUri.Scheme != Uri.UriSchemeHttp && tokenUri.Scheme != Uri.UriSchemeHttps))
                        result.Add(HttpSettingKeys.TokenUrl, "invalid_format", "tokenUrl must be an absolute http or https URL.");
                    string? id = secrets.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.ClientIdField) ?? secrets.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.UsernameField);
                    string? secret = secrets.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.ClientSecretField) ?? secrets.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.PasswordField);
                    if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret))
                        result.Add(HttpSettingKeys.ClientCredentialsSlot, "required", "authType is OAuth2ClientCredentials, so bind a Generic secret with clientId and clientSecret (or a Password secret with username and password) to the 'clientCredentials' slot.");
                    break;
                default:
                    break;
            }
        }

        private static void ValidateRange(ResourceSettings s, ConnectorValidationResult result, string key, long min, long max)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                result.Add(key, "out_of_range", key + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ValidateBool(ResourceSettings s, ConnectorValidationResult result, string key)
        {
            string? raw = s.GetString(key);
            if (raw == null) return;
            string v = raw.Trim().ToLowerInvariant();
            if (v != "true" && v != "false" && v != "1" && v != "0" && v != "yes" && v != "no")
                result.Add(key, "invalid_value", key + " must be true or false.");
        }

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor(HttpSettingKeys.BaseUrl, SettingValueKindEnum.String, "Base URL")
            {
                Required = true,
                Pattern = "^https?://",
                Group = "Connection",
                ShowInSummary = true,
                Help = "Absolute http or https URL every request path is relative to, for example https://api.example.com/v1. No query string or credentials."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.HealthPath, SettingValueKindEnum.String, "Health path")
            {
                Group = "Connection",
                Help = "Path Test Connection probes, relative to the base URL, for example /health. When set, the test passes only on a 2xx or 3xx answer. When empty, the test probes the first allowed path prefix and only checks that the server answers: a 4xx answer still passes as reachable and is flagged in the result."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.DefaultHeaders, SettingValueKindEnum.StringList, "Default headers")
            {
                Group = "Connection",
                Help = "Headers sent with every request, one 'Name: value' per line, for example 'Accept: application/json'. Authorization, cookies, and the API key header are not allowed here; use authType and a secret."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.TimeoutSeconds, SettingValueKindEnum.Integer, "Timeout (seconds)")
            {
                DefaultValue = "30",
                Minimum = 1,
                Maximum = 600,
                Group = "Connection",
                Help = "Longest time one request may take, including redirects. Default 30, minimum 1, maximum 600. The binding's timeout applies when shorter."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.FollowRedirects, SettingValueKindEnum.Boolean, "Follow redirects")
            {
                DefaultValue = "true",
                Group = "Connection",
                Help = "Follow redirects to the same host name (any port), never from https to http, and only inside the base path and the allowed path prefixes. Credentials go only to the resource's own origin (same scheme, host, and port): a hop to another port, or from http to https, is followed without them. Default true."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.MaxRedirects, SettingValueKindEnum.Integer, "Maximum redirects")
            {
                DefaultValue = "5",
                Minimum = 0,
                Maximum = 20,
                Group = "Connection",
                Help = "Most redirects followed per request. Default 5."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.VerifyTls, SettingValueKindEnum.Boolean, "Verify TLS certificates")
            {
                DefaultValue = "true",
                Group = "Connection",
                Help = "When false, invalid or self-signed certificates are accepted (http_health_check still reports them). Keep true for anything reachable from the internet. Default true."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.AuthType, SettingValueKindEnum.Enum, "Authentication")
            {
                DefaultValue = "None",
                AllowedValues = new List<string> { "None", "Basic", "Bearer", "ApiKeyHeader", "OAuth2ClientCredentials" },
                Group = "Authentication",
                ShowInSummary = true,
                Help = "None; Basic (basicAuth slot); Bearer (bearerToken slot); ApiKeyHeader (apiKey slot, sent in apiKeyHeaderName); OAuth2ClientCredentials (clientCredentials slot and tokenUrl; tokens are cached and refreshed)."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.ApiKeyHeaderName, SettingValueKindEnum.String, "API key header")
            {
                DefaultValue = "X-API-Key",
                Group = "Authentication",
                Help = "Header that carries the API key when authType is ApiKeyHeader. Default X-API-Key."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.TokenUrl, SettingValueKindEnum.String, "OAuth2 token URL")
            {
                Pattern = "^https?://",
                Group = "Authentication",
                Help = "Token endpoint for the client credentials grant. Required when authType is OAuth2ClientCredentials."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.Scopes, SettingValueKindEnum.StringList, "OAuth2 scopes")
            {
                Group = "Authentication",
                Help = "Scopes requested with the client credentials grant, one per line or space separated. Empty requests the client's default scopes."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.AllowedMethods, SettingValueKindEnum.StringList, "Allowed methods")
            {
                DefaultValue = String.Join("\n", HttpResourceOptions.DefaultMethods),
                Group = "Safety",
                Help = "HTTP methods http_request may use, one per line. Default GET, HEAD, OPTIONS, POST, PUT, PATCH, DELETE. Remove methods to forbid them outright."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.AllowedPathPrefixes, SettingValueKindEnum.StringList, "Allowed path prefixes")
            {
                DefaultValue = "/",
                Group = "Safety",
                Help = "Path prefixes, relative to the base URL path, that calls may address, one per line, for example /orders. Matching is on whole segments: /orders allows /orders/42 but not /orders-archive. Default / (everything under the base URL)."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.MethodEffectOverrides, SettingValueKindEnum.StringList, "Effect overrides")
            {
                Group = "Safety",
                Help = "Override the effect of matching calls, one 'METHOD /prefix=Effect' per line (METHOD may be *), for example 'POST /search=Read' or 'GET /admin=Destructive'. The longest matching prefix wins. Effects: Read, Write, Destructive."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.MaxResponseBytes, SettingValueKindEnum.Integer, "Max response bytes")
            {
                DefaultValue = "1048576",
                Minimum = 1024,
                Maximum = 64 * 1024 * 1024,
                Group = "Limits",
                Help = "Most response body bytes read per request; the rest is not downloaded. The call's output limits may show less. Default 1 MiB."
            });
            schema.Add(new SettingDescriptor(HttpSettingKeys.MaxRequestBytes, SettingValueKindEnum.Integer, "Max request bytes")
            {
                DefaultValue = "1048576",
                Minimum = 1,
                Maximum = 64 * 1024 * 1024,
                Group = "Limits",
                Help = "Largest request body a call may send. Default 1 MiB."
            });

            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = TypeName;
            d.Family = FamilyName;
            d.DisplayName = "HTTP API";
            d.Description = "Any HTTP or REST API under one base URL: classified requests limited to allowed methods and path prefixes, with Basic, bearer, API key, or OAuth2 client credentials authentication, plus health checks that report status, latency, redirects, and TLS certificate expiry.";
            d.Category = ConnectorCategoryEnum.Http;
            d.IconKey = "globe";
            d.Version = "1.0.0";
            d.SettingsSchema = schema;
            d.SecretSlots = new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(HttpSettingKeys.BasicAuthSlot, "Basic credentials", SecretKindEnum.Password) { Help = "Password secret with fields 'username' and 'password'. Used when authType is Basic." },
                new SecretSlotDescriptor(HttpSettingKeys.BearerTokenSlot, "Bearer token", SecretKindEnum.BearerToken, SecretKindEnum.ApiToken) { Help = "BearerToken (or ApiToken) secret, field 'token', sent as Authorization: Bearer. Used when authType is Bearer." },
                new SecretSlotDescriptor(HttpSettingKeys.ApiKeySlot, "API key", SecretKindEnum.ApiToken) { Help = "ApiToken secret (field 'token') sent in the apiKeyHeaderName header. Used when authType is ApiKeyHeader." },
                new SecretSlotDescriptor(HttpSettingKeys.ClientCredentialsSlot, "OAuth2 client credentials", SecretKindEnum.Generic, SecretKindEnum.Password) { Help = "Generic secret with 'clientId' and 'clientSecret', or a Password secret with 'username' (client ID) and 'password' (client secret). Used when authType is OAuth2ClientCredentials." }
            };
            d.DefaultInstructionsKey = "connector." + TypeName + ".instructions";
            d.DefaultInstructions = EmbeddedText.Read(typeof(HttpConnector).Assembly, "connector.http.instructions.md");
            d.DocumentationUrl = "https://www.rfc-editor.org/rfc/rfc9110";
            return d;
        }

        #endregion
    }
}
