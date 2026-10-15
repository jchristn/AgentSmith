namespace Test.Shared.Connectors.Agents
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

    /// <summary>
    /// Helpers for the agent connector suites (<c>mcp</c> and <c>a2a-agent</c>).
    /// </summary>
    public static class AgentsTestHelper
    {
        #region Public-Methods

        /// <summary>
        /// Build settings from alternating key and value strings.
        /// </summary>
        /// <param name="pairs">Key, value, key, value, ...</param>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings(params string[] pairs)
        {
            Dictionary<string, string> s = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2) s[pairs[i]] = pairs[i + 1];
            return s;
        }

        /// <summary>
        /// Build a resource with a unique identifier (so session pool keys and cached tool lists never collide between
        /// cases) and a private session pool.
        /// </summary>
        /// <param name="connectorType">Connector type.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets, or null.</param>
        /// <param name="pool">Session pool the caller disposes.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(string connectorType, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets, NoPoolSessionPool pool)
        {
            return new ConnectorResourceContext
            {
                ResourceId = "res_" + Guid.NewGuid().ToString("N"),
                TenantId = "ten_test",
                Name = connectorType + "-test",
                ConnectorType = connectorType,
                Settings = new ResourceSettings(settings),
                Secrets = new SecretAccessor(secrets),
                ConfigurationHash = "cfg",
                Sessions = pool
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
                BindingAlias = "remote",
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
        /// Classify a call.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <param name="context">Context.</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Classify(IConnector connector, string name, string json, ConnectorInvocationContext context)
        {
            return Tool(connector, name).Classify(context, new ToolArguments(json));
        }

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string Json(object value)
        {
            return JsonSerializer.Serialize(value);
        }

        /// <summary>
        /// A free port on 127.0.0.1.
        /// </summary>
        /// <returns>Port.</returns>
        public static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// Wait until something accepts TCP connections on 127.0.0.1 at a port.
        /// </summary>
        /// <param name="port">Port.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">Thrown when nothing listens within five seconds.</exception>
        public static async Task WaitForPortAsync(int port, CancellationToken token)
        {
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        await client.ConnectAsync(IPAddress.Loopback, port, token).ConfigureAwait(false);
                        return;
                    }
                }
                catch (SocketException)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException("Nothing listens on 127.0.0.1:" + port + ".");
        }

        /// <summary>
        /// Read a top-level property of a structured result as text (strings unquoted, other values as raw JSON), or null.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property name.</param>
        /// <returns>Value or null.</returns>
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
        /// Assert that text contains a fragment.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="fragment">Fragment.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when missing.</exception>
        public static void AssertContains(string? text, string fragment, string what)
        {
            if (text == null || !text.Contains(fragment, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " should contain '" + fragment + "' but was: " + (text ?? "(null)"));
        }

        /// <summary>
        /// Assert that no secret value appears in a result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="secret">Secret value.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the secret leaks.</exception>
        public static void AssertNoSecret(ToolResult result, string secret, string what)
        {
            if (result.Text.Contains(secret, StringComparison.Ordinal) || (result.StructuredJson ?? String.Empty).Contains(secret, StringComparison.Ordinal))
                throw new InvalidOperationException("Assertion failed: " + what + " leaked a secret value.");
        }

        #endregion
    }
}
