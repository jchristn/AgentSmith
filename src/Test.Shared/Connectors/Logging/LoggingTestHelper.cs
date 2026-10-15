namespace Test.Shared.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Helpers for the logging connector suites: resource and context factories, tool invocation, and assertions.
    /// </summary>
    public static class LoggingTestHelper
    {
        #region Public-Methods

        /// <summary>
        /// Build a resource context.
        /// </summary>
        /// <param name="connectorType">Connector type.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="token">API token, or null to bind no secret.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(string connectorType, Dictionary<string, string> settings, string? token)
        {
            Dictionary<string, Dictionary<string, string>> slots = new Dictionary<string, Dictionary<string, string>>();
            if (token != null) slots["apiToken"] = new Dictionary<string, string> { { "token", token } };
            return new ConnectorResourceContext
            {
                ResourceId = "res_test",
                TenantId = "ten_test",
                Name = "logs-test",
                ConnectorType = connectorType,
                Settings = new ResourceSettings(settings),
                Secrets = new SecretAccessor(slots)
            };
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="timeoutSeconds">Timeout in seconds. Default 30.</param>
        /// <param name="limits">Output limits, or null for defaults.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, double timeoutSeconds = 30, OutputLimits? limits = null)
        {
            return new ConnectorInvocationContext
            {
                Resource = resource,
                BindingAlias = "logs",
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
        /// <param name="argumentsJson">Arguments JSON.</param>
        /// <param name="context">Context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public static Task<ToolResult> RunAsync(IConnector connector, string name, string argumentsJson, ConnectorInvocationContext context, CancellationToken token)
        {
            return Tool(connector, name).ExecuteAsync(context, new ToolArguments(argumentsJson), token);
        }

        /// <summary>
        /// Read the string property of a structured result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property name.</param>
        /// <returns>Value or null.</returns>
        public static string? StructuredString(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return null;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty(property, out JsonElement e) || e.ValueKind != JsonValueKind.String) return null;
                return e.GetString();
            }
        }

        /// <summary>
        /// Read the event IDs of a structured log result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>IDs in order.</returns>
        public static List<string> EventIds(ToolResult result)
        {
            List<string> ids = new List<string>();
            if (result.StructuredJson == null) return ids;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty("events", out JsonElement events)) return ids;
                foreach (JsonElement e in events.EnumerateArray()) ids.Add(e.GetProperty("id").GetString() ?? String.Empty);
            }
            return ids;
        }

        /// <summary>
        /// Read a numeric property of a structured result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property name.</param>
        /// <returns>Value, or -1 when absent.</returns>
        public static long StructuredLong(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return -1;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty(property, out JsonElement e) || e.ValueKind != JsonValueKind.Number) return -1;
                return e.GetInt64();
            }
        }

        /// <summary>
        /// Read a boolean property of a structured result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property name.</param>
        /// <returns>Value; false when absent.</returns>
        public static bool StructuredBool(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return false;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                return doc.RootElement.TryGetProperty(property, out JsonElement e) && e.ValueKind == JsonValueKind.True;
            }
        }

        /// <summary>
        /// Fail with a message when a condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Assert that a result succeeded.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void AssertOk(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Assert that a result failed with a code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown otherwise.</exception>
        public static void AssertError(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected error " + code + " but got " + (result.IsError ? result.ErrorCode : "success") + ": " + result.Text);
        }

        #endregion
    }
}
