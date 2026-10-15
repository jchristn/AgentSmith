namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Redis;

    /// <summary>
    /// Helpers for the Redis suites: resource and context factories, tool invocation, and assertions.
    /// </summary>
    public static class RedisTestHelper
    {
        #region Public-Members

        /// <summary>
        /// Shared connector instance.
        /// </summary>
        public static RedisConnector Connector { get; } = new RedisConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a resource.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="password">Password, or null to bind no secret.</param>
        /// <param name="pool">Session pool, or null for a fresh unpooled pool.</param>
        /// <param name="secretUsername">Username stored with the password secret, or null.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(Dictionary<string, string> settings, string? password, IConnectorSessionPool? pool = null, string? secretUsername = null)
        {
            Dictionary<string, Dictionary<string, string>> slots = new Dictionary<string, Dictionary<string, string>>();
            if (password != null)
            {
                Dictionary<string, string> fields = new Dictionary<string, string> { { "password", password } };
                if (secretUsername != null) fields["username"] = secretUsername;
                slots["password"] = fields;
            }
            ConnectorResourceContext r = new ConnectorResourceContext
            {
                ResourceId = "res_redis_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                TenantId = "ten_test",
                Name = "cache",
                ConnectorType = "redis",
                Settings = new ResourceSettings(settings),
                Secrets = new SecretAccessor(slots),
                ConfigurationHash = "h"
            };
            if (pool != null) r.Sessions = pool;
            return r;
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="mode">Access mode. Default ReadWrite.</param>
        /// <param name="timeoutSeconds">Timeout. Default 30.</param>
        /// <param name="limits">Limits, or null for defaults.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, AccessModeEnum mode = AccessModeEnum.ReadWrite, double timeoutSeconds = 30, OutputLimits? limits = null)
        {
            return new ConnectorInvocationContext
            {
                Resource = resource,
                BindingAlias = "cache",
                AccessMode = mode,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Limits = limits ?? new OutputLimits()
            };
        }

        /// <summary>
        /// Find a tool.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>Tool.</returns>
        /// <exception cref="InvalidOperationException">Thrown when missing.</exception>
        public static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        /// <summary>
        /// Run a tool.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments.</param>
        /// <param name="context">Context.</param>
        /// <param name="token">Token.</param>
        /// <returns>Result.</returns>
        public static Task<ToolResult> RunAsync(string name, string json, ConnectorInvocationContext context, CancellationToken token)
        {
            return Tool(name).ExecuteAsync(context, new ToolArguments(json), token);
        }

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments.</param>
        /// <param name="context">Context, or null.</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Classify(string name, string json, ConnectorInvocationContext? context)
        {
            return Tool(name).Classify(context!, new ToolArguments(json));
        }

        /// <summary>
        /// Serialize a value to JSON.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON.</returns>
        public static string Json(object value)
        {
            return JsonSerializer.Serialize(value);
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
        /// <param name="code">Code.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown otherwise.</exception>
        public static void AssertError(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected " + code + " but got " + (result.IsError ? result.ErrorCode : "success") + ": " + result.Text);
        }

        /// <summary>
        /// Read a top-level property of a structured result as raw JSON text, or null.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property.</param>
        /// <returns>Raw JSON or null.</returns>
        public static string? Raw(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return null;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                return doc.RootElement.TryGetProperty(property, out JsonElement e) ? e.GetRawText() : null;
            }
        }

        /// <summary>
        /// Read a top-level string property of a structured result, or null.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property.</param>
        /// <returns>Value or null.</returns>
        public static string? Str(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return null;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                return doc.RootElement.TryGetProperty(property, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            }
        }

        /// <summary>
        /// Read a top-level numeric property of a structured result, or -999.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="property">Property.</param>
        /// <returns>Value.</returns>
        public static long Num(ToolResult result, string property)
        {
            if (result.StructuredJson == null) return -999;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                return doc.RootElement.TryGetProperty(property, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt64() : -999;
            }
        }

        /// <summary>
        /// Rows of a structured table result (column 0 values, or the requested column).
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="column">Column index.</param>
        /// <returns>Cells.</returns>
        public static List<string?> Column(ToolResult result, int column)
        {
            List<string?> cells = new List<string?>();
            if (result.StructuredJson == null) return cells;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                if (!doc.RootElement.TryGetProperty("rows", out JsonElement rows)) return cells;
                foreach (JsonElement row in rows.EnumerateArray())
                {
                    JsonElement cell = row[column];
                    cells.Add(cell.ValueKind == JsonValueKind.Null ? null : cell.GetString());
                }
            }
            return cells;
        }

        /// <summary>
        /// Keys of a structured scan result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>Keys.</returns>
        public static List<string> ScanKeys(ToolResult result)
        {
            List<string> keys = new List<string>();
            if (result.StructuredJson == null) return keys;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                foreach (JsonElement k in doc.RootElement.GetProperty("keys").EnumerateArray()) keys.Add(k.GetProperty("key").GetString() ?? String.Empty);
            }
            return keys;
        }

        /// <summary>
        /// Count of items in a structured get result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>Count, or -1 when absent.</returns>
        public static int ItemCount(ToolResult result)
        {
            if (result.StructuredJson == null) return -1;
            using (JsonDocument doc = JsonDocument.Parse(result.StructuredJson))
            {
                return doc.RootElement.TryGetProperty("items", out JsonElement items) ? items.GetArrayLength() : -1;
            }
        }

        #endregion
    }
}
