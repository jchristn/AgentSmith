namespace Test.Shared.Connectors.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Storage;

    /// <summary>
    /// Resource factories and invocation helpers for the storage testbed (docker/testbed/storage/compose.yaml) and for
    /// offline unit cases. Ports default to 28900 (MinIO), 28910 (Azurite), and 28920 (fake-gcs-server) and can be
    /// overridden with HUGO_TESTBED_S3_PORT, HUGO_TESTBED_AZURITE_PORT, and HUGO_TESTBED_GCS_PORT.
    /// </summary>
    public static class StorageTestbed
    {
        #region Public-Members

        /// <summary>
        /// Bucket or container seeded by the testbed.
        /// </summary>
        public const string Container = "hugo-testbed";

        /// <summary>
        /// MinIO access key.
        /// </summary>
        public const string S3AccessKey = "hugo-testbed";

        /// <summary>
        /// MinIO secret key.
        /// </summary>
        public const string S3SecretKey = "hugo-testbed-secret";

        /// <summary>
        /// Azurite account.
        /// </summary>
        public const string AzuriteAccount = "devstoreaccount1";

        /// <summary>
        /// Azurite well-known development key (public, documented by Microsoft).
        /// </summary>
        public const string AzuriteKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        /// <summary>
        /// Reason testbed cases are skipped.
        /// </summary>
        public const string SkipReason = "storage testbed disabled: start docker/testbed/storage/compose.yaml and set HUGO_TESTBED=1";

        /// <summary>
        /// Whether testbed suites run.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED") == "1";
            }
        }

        /// <summary>
        /// MinIO S3 port.
        /// </summary>
        public static int S3Port
        {
            get
            {
                return Port("HUGO_TESTBED_S3_PORT", 28900);
            }
        }

        /// <summary>
        /// Azurite blob port.
        /// </summary>
        public static int AzuritePort
        {
            get
            {
                return Port("HUGO_TESTBED_AZURITE_PORT", 28910);
            }
        }

        /// <summary>
        /// fake-gcs-server port.
        /// </summary>
        public static int GcsPort
        {
            get
            {
                return Port("HUGO_TESTBED_GCS_PORT", 28920);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<string> _GcsKeyJson = new Lazy<string>(BuildGcsKeyJson, LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the connector for a provider.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Connector.</returns>
        public static StorageConnectorBase Connector(StorageProviderEnum provider)
        {
            switch (provider)
            {
                case StorageProviderEnum.AmazonS3:
                    return new S3Connector();
                case StorageProviderEnum.AzureBlob:
                    return new AzureBlobConnector();
                default:
                    return new GcsConnector();
            }
        }

        /// <summary>
        /// Short name of a provider for suite identifiers.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <returns>Connector type.</returns>
        public static string TypeOf(StorageProviderEnum provider)
        {
            switch (provider)
            {
                case StorageProviderEnum.AmazonS3:
                    return S3Connector.ConnectorType;
                case StorageProviderEnum.AzureBlob:
                    return AzureBlobConnector.ConnectorType;
                default:
                    return GcsConnector.ConnectorType;
            }
        }

        /// <summary>
        /// Build a testbed resource.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="variant">Variant name; distinct variants get distinct pooled sessions.</param>
        /// <param name="pool">Session pool.</param>
        /// <param name="settings">Extra or overriding settings, or null.</param>
        /// <param name="badCredentials">Use wrong credentials.</param>
        /// <returns>Resource.</returns>
        public static ConnectorResourceContext Resource(
            StorageProviderEnum provider,
            string variant,
            IConnectorSessionPool pool,
            Dictionary<string, string>? settings = null,
            bool badCredentials = false)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, Dictionary<string, string>> secrets = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            switch (provider)
            {
                case StorageProviderEnum.AmazonS3:
                    values["bucket"] = Container;
                    values["region"] = "us-east-1";
                    values["endpoint"] = "http://127.0.0.1:" + S3Port.ToString(CultureInfo.InvariantCulture);
                    values["useSsl"] = "false";
                    secrets["accessKeyPair"] = new Dictionary<string, string>
                    {
                        { "accessKeyId", S3AccessKey },
                        { "secretAccessKey", badCredentials ? "wrong-secret-key" : S3SecretKey }
                    };
                    break;
                case StorageProviderEnum.AzureBlob:
                    values["accountName"] = AzuriteAccount;
                    values["container"] = Container;
                    values["endpoint"] = "http://127.0.0.1:" + AzuritePort.ToString(CultureInfo.InvariantCulture) + "/" + AzuriteAccount;
                    secrets["accessKey"] = new Dictionary<string, string>
                    {
                        { "token", badCredentials ? Convert.ToBase64String(new byte[64]) : AzuriteKey }
                    };
                    break;
                default:
                    values["projectId"] = "hugo-testbed";
                    values["bucket"] = Container;
                    values["customEndpoint"] = "http://127.0.0.1:" + GcsPort.ToString(CultureInfo.InvariantCulture);
                    secrets["serviceAccountJson"] = new Dictionary<string, string> { { "json", _GcsKeyJson.Value } };
                    break;
            }

            if (settings != null)
            {
                foreach (KeyValuePair<string, string> kv in settings) values[kv.Key] = kv.Value;
            }

            ConnectorResourceContext resource = new ConnectorResourceContext();
            resource.ResourceId = "res_test_" + TypeOf(provider) + "_" + variant;
            resource.Name = TypeOf(provider) + "-" + variant;
            resource.ConnectorType = TypeOf(provider);
            resource.Settings = new ResourceSettings(values);
            resource.Secrets = new SecretAccessor(secrets);
            resource.ConfigurationHash = variant + (badCredentials ? "-bad" : String.Empty) + "-" + String.Join(",", values.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
            resource.Sessions = pool;
            return resource;
        }

        /// <summary>
        /// Build an invocation context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="limits">Output limits, or null for defaults.</param>
        /// <param name="access">Access mode.</param>
        /// <returns>Context.</returns>
        public static ConnectorInvocationContext Context(ConnectorResourceContext resource, OutputLimits? limits = null, AccessModeEnum access = AccessModeEnum.ReadWrite)
        {
            ConnectorInvocationContext context = new ConnectorInvocationContext();
            context.Resource = resource;
            context.BindingAlias = resource.Name;
            context.AccessMode = access;
            context.Limits = limits ?? new OutputLimits();
            context.Timeout = TimeSpan.FromSeconds(60);
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
            ArgumentNullException.ThrowIfNull(connector);
            IConnectorTool? tool = connector.Tools.FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) throw new InvalidOperationException("Tool " + name + " not found.");
            return tool;
        }

        /// <summary>
        /// Invoke a tool.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="context">Invocation context.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        public static Task<ToolResult> InvokeAsync(IConnector connector, ConnectorInvocationContext context, string name, string json, CancellationToken token = default)
        {
            return Tool(connector, name).ExecuteAsync(context, new ToolArguments(json), token);
        }

        /// <summary>
        /// Classify a call.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="context">Invocation context.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="json">Arguments JSON.</param>
        /// <returns>Classification.</returns>
        public static EffectClassification Classify(IConnector connector, ConnectorInvocationContext context, string name, string json)
        {
            return Tool(connector, name).Classify(context, new ToolArguments(json));
        }

        /// <summary>
        /// Serialize a value as a JSON string literal for building argument objects.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON string literal.</returns>
        public static string Quote(string value)
        {
            return JsonSerializer.Serialize(value);
        }

        /// <summary>
        /// A syntactically valid service account key with a fresh RSA key. fake-gcs-server accepts any credentials.
        /// </summary>
        /// <returns>JSON.</returns>
        public static string GcsKeyJson()
        {
            return _GcsKeyJson.Value;
        }

        #endregion

        #region Private-Methods

        private static int Port(string variable, int fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!String.IsNullOrEmpty(value) && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0 && port < 65536) return port;
            return fallback;
        }

        private static string BuildGcsKeyJson()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                Dictionary<string, string> key = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "type", "service_account" },
                    { "project_id", "hugo-testbed" },
                    { "private_key_id", "hugo-testbed-key" },
                    { "private_key", rsa.ExportPkcs8PrivateKeyPem() },
                    { "client_email", "hugo-testbed@hugo-testbed.iam.gserviceaccount.com" },
                    { "client_id", "100000000000000000000" },
                    { "token_uri", "https://oauth2.googleapis.com/token" }
                };
                return JsonSerializer.Serialize(key);
            }
        }

        #endregion
    }
}
