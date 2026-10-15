namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Http;

    /// <summary>
    /// Helpers for the <c>http</c> connector suites.
    /// </summary>
    public static class HttpTestHelper
    {
        #region Public-Members

        /// <summary>
        /// Bearer token used by the stub tests.
        /// </summary>
        public static readonly string BearerToken = "stub-bearer-token-9f8e7d";

        /// <summary>
        /// API key used by the stub tests.
        /// </summary>
        public static readonly string ApiKey = "stub-api-key-1a2b3c";

        /// <summary>
        /// Basic password used by the stub tests.
        /// </summary>
        public static readonly string BasicPassword = "stub-basic-password-77";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a settings dictionary for a base URL with extra entries.
        /// </summary>
        /// <param name="baseUrl">Base URL.</param>
        /// <param name="extra">Extra settings as alternating key, value.</param>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings(string baseUrl, params string[] extra)
        {
            Dictionary<string, string> s = new Dictionary<string, string> { { HttpSettingKeys.BaseUrl, baseUrl } };
            for (int i = 0; i + 1 < extra.Length; i += 2) s[extra[i]] = extra[i + 1];
            return s;
        }

        /// <summary>
        /// Secrets with every slot bound to the stub test values.
        /// </summary>
        /// <param name="stub">Stub supplying OAuth2 client credentials, or null.</param>
        /// <returns>Slots.</returns>
        public static Dictionary<string, Dictionary<string, string>> AllSecrets(HttpStubServer? stub)
        {
            return AllSecrets(stub?.ClientId, stub?.ClientSecret);
        }

        /// <summary>
        /// Secrets with every slot bound, using the given OAuth2 client credentials.
        /// </summary>
        /// <param name="clientId">Client ID, or null to leave the clientCredentials slot unbound.</param>
        /// <param name="clientSecret">Client secret.</param>
        /// <returns>Slots.</returns>
        public static Dictionary<string, Dictionary<string, string>> AllSecrets(string? clientId, string? clientSecret)
        {
            Dictionary<string, Dictionary<string, string>> slots = new Dictionary<string, Dictionary<string, string>>
            {
                { HttpSettingKeys.BasicAuthSlot, new Dictionary<string, string> { { "username", "stub-user" }, { "password", BasicPassword } } },
                { HttpSettingKeys.BearerTokenSlot, new Dictionary<string, string> { { "token", BearerToken } } },
                { HttpSettingKeys.ApiKeySlot, new Dictionary<string, string> { { "token", ApiKey } } }
            };
            if (clientId != null && clientSecret != null) slots[HttpSettingKeys.ClientCredentialsSlot] = new Dictionary<string, string> { { "clientId", clientId }, { "clientSecret", clientSecret } };
            return slots;
        }

        /// <summary>
        /// Build a resource context.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets, or null for none.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets = null)
        {
            return new ConnectorResourceContext
            {
                ResourceId = "res_http_test",
                TenantId = "ten_test",
                Name = "api-test",
                ConnectorType = "http",
                Settings = new ResourceSettings(settings),
                Secrets = new SecretAccessor(secrets)
            };
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="timeoutSeconds">Timeout. Default 30.</param>
        /// <param name="limits">Limits, or null for defaults.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, double timeoutSeconds = 30, OutputLimits? limits = null)
        {
            return new ConnectorInvocationContext
            {
                Resource = resource,
                BindingAlias = "api",
                AccessMode = AccessModeEnum.ReadWrite,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Limits = limits ?? new OutputLimits()
            };
        }

        /// <summary>
        /// Find a tool.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="name">Tool name.</param>
        /// <returns>Tool.</returns>
        /// <exception cref="InvalidOperationException">Thrown when missing.</exception>
        public static IConnectorTool Tool(IConnector connector, string name)
        {
            IConnectorTool? tool = connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        /// <summary>
        /// Run a tool.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <param name="context">Context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public static Task<ToolResult> RunAsync(IConnector connector, string name, string json, ConnectorInvocationContext context, CancellationToken token)
        {
            return Tool(connector, name).ExecuteAsync(context, new ToolArguments(json), token);
        }

        /// <summary>
        /// Serialize an object to JSON arguments.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string Json(object value)
        {
            return JsonSerializer.Serialize(value);
        }

        /// <summary>
        /// A port on 127.0.0.1 with nothing listening.
        /// </summary>
        /// <returns>Port.</returns>
        public static int ClosedPort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// Read a property of a structured result as raw JSON text, or null.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property name.</param>
        /// <returns>Raw text or null.</returns>
        public static string? Structured(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return null;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty(property, out JsonElement e)) return null;
                return e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
            }
        }

        /// <summary>
        /// Fail when a condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when false.</exception>
        public static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert success.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown on error.</exception>
        public static void AssertOk(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Assert an error code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown otherwise.</exception>
        public static void AssertError(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected " + code + " but got " + (result.IsError ? result.ErrorCode : "success") + ": " + result.Text);
        }

        /// <summary>
        /// Assert that no secret value appears in a result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="secrets">Secret values.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when a secret leaks.</exception>
        public static void AssertNoSecrets(ToolResult result, IEnumerable<string> secrets, string what)
        {
            foreach (string s in secrets)
            {
                if (result.Text.Contains(s, StringComparison.Ordinal) || (result.StructuredJson ?? String.Empty).Contains(s, StringComparison.Ordinal))
                    throw new InvalidOperationException("Assertion failed: " + what + " leaked a secret value.");
            }
        }

        #endregion
    }
}
