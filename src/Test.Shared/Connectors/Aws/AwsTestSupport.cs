namespace Test.Shared.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws;

    /// <summary>
    /// Resource factories and invocation helpers shared by the AWS suites.
    /// </summary>
    public static class AwsTestSupport
    {
        #region Public-Members

        /// <summary>
        /// Access key ID used by test resources.
        /// </summary>
        public const string AccessKeyId = "AKIAHUGOTESTSTUB0001";

        /// <summary>
        /// Secret access key used by test resources; must never appear in any output.
        /// </summary>
        public const string SecretAccessKey = "hugo-test-secret-access-key-do-not-print-0001";

        /// <summary>
        /// A closed loopback endpoint for offline cases.
        /// </summary>
        public const string ClosedEndpoint = "http://127.0.0.1:9";

        /// <summary>
        /// The connector under test.
        /// </summary>
        public static AwsConnector Connector { get; } = new AwsConnector();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a resource with an access key pair and settings (region us-east-1, no SDK retries unless overridden).
        /// </summary>
        /// <param name="endpoint">Endpoint override, or null.</param>
        /// <param name="settings">Extra or replacement settings, or null.</param>
        /// <param name="withKeys">Whether to bind the access key pair. Default true.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(string? endpoint, Dictionary<string, string>? settings = null, bool withKeys = true)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            values[AwsSettingKeys.Region] = "us-east-1";
            values[AwsSettingKeys.MaxRetries] = "0";
            if (!String.IsNullOrEmpty(endpoint)) values[AwsSettingKeys.EndpointOverride] = endpoint;
            if (settings != null) foreach (KeyValuePair<string, string> kv in settings) values[kv.Key] = kv.Value;

            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (withKeys)
            {
                secrets[AwsSettingKeys.SlotAccessKeyPair] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { AwsSettingKeys.FieldAccessKeyId, AccessKeyId },
                    { AwsSettingKeys.FieldSecretAccessKey, SecretAccessKey }
                };
            }

            ConnectorResourceContext resource = new ConnectorResourceContext();
            resource.ResourceId = "res_aws_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            resource.TenantId = "ten_test";
            resource.Name = "aws-test";
            resource.ConnectorType = AwsConnector.ConnectorType;
            resource.ConfigurationHash = Guid.NewGuid().ToString("N").Substring(0, 16);
            resource.Settings = new ResourceSettings(values);
            resource.Secrets = new SecretAccessor(secrets);
            resource.Sessions = new NoPoolSessionPool();
            return resource;
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="timeout">Timeout. Default 30 seconds.</param>
        /// <param name="access">Access mode. Default ReadWrite.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, TimeSpan? timeout = null, AccessModeEnum access = AccessModeEnum.ReadWrite)
        {
            ConnectorInvocationContext context = new ConnectorInvocationContext();
            context.Resource = resource;
            context.BindingAlias = "aws-prod";
            context.AccessMode = access;
            context.Timeout = timeout ?? TimeSpan.FromSeconds(30);
            return context;
        }

        /// <summary>
        /// Find a tool by name.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <returns>Tool.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the tool does not exist.</exception>
        public static IConnectorTool Tool(string name)
        {
            IConnectorTool? tool = Connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="tool">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Classify(ConnectorInvocationContext context, string tool, string json)
        {
            return Tool(tool).Classify(context, new ToolArguments(json));
        }

        /// <summary>
        /// Execute a call.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="tool">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public static Task<ToolResult> InvokeAsync(ConnectorInvocationContext context, string tool, string json, CancellationToken token = default)
        {
            return Tool(tool).ExecuteAsync(context, new ToolArguments(json), token);
        }

        /// <summary>
        /// Assert a result's text, structured JSON, and error text never contain the test secret.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="what">What was called.</param>
        public static void AssertNoSecret(ToolResult result, string what)
        {
            AwsAssert.NotContains(result.Text, SecretAccessKey, what + " text");
            AwsAssert.NotContains(result.StructuredJson, SecretAccessKey, what + " structured output");
        }

        #endregion
    }
}
