namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Helpers shared by the SQL connector suites: contexts, tool invocation, assertions, and repository paths.
    /// Thread-safe.
    /// </summary>
    public static class SqlTestSupport
    {
        #region Public-Methods

        /// <summary>
        /// Whether testbed suites should run (environment variable <c>HUGO_TESTBED=1</c>).
        /// </summary>
        /// <returns>True when enabled.</returns>
        public static bool TestbedEnabled()
        {
            return String.Equals(Environment.GetEnvironmentVariable("HUGO_TESTBED"), "1", StringComparison.Ordinal);
        }

        /// <summary>
        /// Build a resource context.
        /// </summary>
        /// <param name="type">Connector type.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="secrets">Secrets by slot then field, or null.</param>
        /// <returns>Resource context.</returns>
        public static ConnectorResourceContext Resource(string type, Dictionary<string, string> settings, Dictionary<string, Dictionary<string, string>>? secrets = null)
        {
            ConnectorResourceContext resource = new ConnectorResourceContext();
            resource.ResourceId = "res_test_" + type;
            resource.TenantId = "ten_test";
            resource.Name = "test-" + type;
            resource.ConnectorType = type;
            resource.Settings = new ResourceSettings(settings);
            resource.Secrets = new SecretAccessor(secrets);
            resource.ConfigurationHash = Guid.NewGuid().ToString("N");
            return resource;
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="mode">Access mode.</param>
        /// <param name="timeoutSeconds">Invocation timeout.</param>
        /// <param name="maxRows">Invocation row limit.</param>
        /// <param name="maxBytes">Invocation byte limit.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, AccessModeEnum mode, int timeoutSeconds = 60, int maxRows = 500, int maxBytes = 262144)
        {
            ConnectorInvocationContext context = new ConnectorInvocationContext();
            context.Resource = resource;
            context.AccessMode = mode;
            context.BindingAlias = resource.Name;
            context.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            context.Limits = new OutputLimits { MaxRows = maxRows, MaxBytes = maxBytes };
            context.CorrelationId = "test";
            return context;
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
        public static Task<ToolResult> RunAsync(IConnector connector, string name, string argumentsJson, ConnectorInvocationContext context, CancellationToken token = default)
        {
            return Tool(connector, name).ExecuteAsync(context, new ToolArguments(argumentsJson), token);
        }

        /// <summary>
        /// Encode a value as a JSON string literal.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON literal.</returns>
        public static string Json(string value)
        {
            return System.Text.Json.JsonSerializer.Serialize(value);
        }

        /// <summary>
        /// Throw when a condition is false.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }

        /// <summary>
        /// Throw unless a result succeeded.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when the result is an error.</exception>
        public static void Ok(ToolResult result, string what)
        {
            if (result.IsError) throw new InvalidOperationException("Assertion failed: " + what + " returned " + result.ErrorCode + ": " + result.Text);
        }

        /// <summary>
        /// Throw unless a result is an error with the given code.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="code">Expected code.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown otherwise.</exception>
        public static void Error(ToolResult result, string code, string what)
        {
            if (!result.IsError || result.ErrorCode != code)
                throw new InvalidOperationException("Assertion failed: " + what + " expected error " + code + " but got " + (result.IsError ? result.ErrorCode : "success") + ": " + result.Text);
        }

        /// <summary>
        /// Locate a file relative to the repository root by walking up from the test binaries.
        /// </summary>
        /// <param name="relativePath">Path relative to the repository root.</param>
        /// <returns>Absolute path.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file cannot be found.</exception>
        public static string RepositoryFile(string relativePath)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Could not find " + relativePath + " above " + AppContext.BaseDirectory + ".");
        }

        /// <summary>
        /// Measure how long an async action takes.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>Elapsed time.</returns>
        public static async Task<TimeSpan> TimeAsync(Func<Task> action)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            await action().ConfigureAwait(false);
            return sw.Elapsed;
        }

        #endregion
    }
}
