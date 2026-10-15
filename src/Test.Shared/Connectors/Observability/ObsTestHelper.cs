namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Helpers for the observability connector suites: resource and context factories, tool invocation, structured
    /// result readers, and assertions.
    /// </summary>
    public static class ObsTestHelper
    {
        #region Public-Methods

        /// <summary>
        /// Build a resource context.
        /// </summary>
        /// <param name="connectorType">Connector type.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets by slot, or null.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(string connectorType, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets = null)
        {
            return new ConnectorResourceContext
            {
                ResourceId = "res_obs",
                TenantId = "ten_test",
                Name = "obs-test",
                ConnectorType = connectorType,
                Settings = new ResourceSettings(settings),
                Secrets = new SecretAccessor(secrets)
            };
        }

        /// <summary>
        /// Settings pointing at a stub.
        /// </summary>
        /// <param name="stub">Stub.</param>
        /// <param name="extra">Extra settings, or null.</param>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings(ObservabilityStub stub, Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> s = new Dictionary<string, string> { { "baseUrl", stub.BaseAddress } };
            if (extra != null) foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            return s;
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="timeoutSeconds">Timeout in seconds.</param>
        /// <param name="limits">Output limits, or null.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, double timeoutSeconds = 30, OutputLimits? limits = null)
        {
            return new ConnectorInvocationContext
            {
                Resource = resource,
                BindingAlias = "obs",
                AccessMode = AccessModeEnum.ReadOnly,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Limits = limits ?? new OutputLimits()
            };
        }

        /// <summary>
        /// Find a tool by name.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="name">Tool name.</param>
        /// <returns>Tool.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the tool does not exist.</exception>
        public static IConnectorTool Tool(IConnector connector, string name)
        {
            IConnectorTool? tool = connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found on " + connector.Descriptor.Type + ".");
            return tool;
        }

        /// <summary>
        /// Invoke a tool.
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
        /// Read a numeric property of a structured result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property.</param>
        /// <returns>Value, or -1.</returns>
        public static long Long(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return -1;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty(property, out JsonElement e) || e.ValueKind != JsonValueKind.Number) return -1;
                return e.GetInt64();
            }
        }

        /// <summary>
        /// Length of an array property of a structured result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property.</param>
        /// <returns>Length, or -1.</returns>
        public static int ArrayLength(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return -1;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty(property, out JsonElement e) || e.ValueKind != JsonValueKind.Array) return -1;
                return e.GetArrayLength();
            }
        }

        /// <summary>
        /// Fail when a condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert a successful result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void Ok(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Assert an error result with a code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown otherwise.</exception>
        public static void Error(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected " + code + " but got " + (result.IsError ? result.ErrorCode : "success") + ": " + result.Text);
        }

        #endregion
    }
}
