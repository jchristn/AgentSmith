namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Elasticsearch and OpenSearch connector (type <c>elasticsearch</c>, family <c>es</c>): Query DSL search and count
    /// against an index allow list, cluster health, index listing, and mappings. Every tool is Read. Thread-safe.
    /// </summary>
    public class ElasticsearchConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "elasticsearch";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "es";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Timeout of the <c>TestConnectionAsync</c> probe. Minimum 1 second, maximum 5 minutes, default 15 seconds.
        /// </summary>
        public TimeSpan TestConnectionTimeout
        {
            get
            {
                return _TestConnectionTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(5)) value = TimeSpan.FromMinutes(5);
                _TestConnectionTimeout = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _TestConnectionTimeout = TimeSpan.FromSeconds(15);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ElasticsearchConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new EsSearchTool(),
                new EsCountTool(),
                new EsClusterHealthTool(),
                new EsIndicesTool(),
                new EsMappingsTool()
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
            ObservabilitySettingsSupport.ValidateCommon(resource, result);
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxHits, 1, 10000);
            string flavor = resource.Settings.GetString(ObservabilitySettingKeys.Flavor, "Elasticsearch") ?? "Elasticsearch";
            if (!Enum.TryParse(flavor, true, out ElasticsearchFlavorEnum parsed) || !Enum.IsDefined(typeof(ElasticsearchFlavorEnum), parsed))
                result.Add(ObservabilitySettingKeys.Flavor, "invalid_value", "flavor must be Elasticsearch or OpenSearch.");
            foreach (string pattern in resource.Settings.GetStringList(ObservabilitySettingKeys.AllowedIndexPatterns))
            {
                EsIndexCheck check = EsIndexGuard.Check(pattern, new List<string> { pattern.StartsWith('.') ? pattern : "*" });
                if (!check.Allowed || pattern.Contains(','))
                {
                    result.Add(ObservabilitySettingKeys.AllowedIndexPatterns, "invalid_format", "Pattern '" + pattern + "' is not a valid index glob. Use one lowercase name or * pattern per line, for example logs-*.");
                    break;
                }
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(_TestConnectionTimeout);
                try
                {
                    ElasticsearchSettings settings = new ElasticsearchSettings(resource.Settings);
                    ObservabilityTransport transport = EsToolBase.Transport(resource, settings);
                    EsRootInfo root = ObservabilityJson.DeserializeUpstream<EsRootInfo>(await transport.GetAsync(String.Empty, null, timeout.Token).ConfigureAwait(false), transport.Provider);
                    EsClusterHealth health = ObservabilityJson.DeserializeUpstream<EsClusterHealth>(await transport.GetAsync("_cluster/health", null, timeout.Token).ConfigureAwait(false), transport.Provider);

                    bool isOpenSearch = root.Version != null && String.Equals(root.Version.Distribution, "opensearch", StringComparison.OrdinalIgnoreCase);
                    ElasticsearchFlavorEnum detected = isOpenSearch ? ElasticsearchFlavorEnum.OpenSearch : ElasticsearchFlavorEnum.Elasticsearch;
                    string version = root.Version == null ? "unknown" : (root.Version.Number ?? "unknown");
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok(
                        "Connected to " + EsToolBase.ProviderName(detected) + " " + version + ", cluster " + (health.ClusterName ?? root.ClusterName ?? "unknown") + " is " + (health.Status ?? "unknown") + ".",
                        sw.Elapsed.TotalMilliseconds);
                    ok.Details["version"] = version;
                    ok.Details["distribution"] = detected.ToString();
                    ok.Details["clusterName"] = health.ClusterName ?? root.ClusterName ?? String.Empty;
                    ok.Details["status"] = health.Status ?? String.Empty;
                    ok.Details["nodes"] = health.NumberOfNodes.ToString(CultureInfo.InvariantCulture);
                    if (detected != settings.Flavor)
                        ok.Details["flavorWarning"] = "The resource's flavor is " + settings.Flavor + " but the server reports " + detected + ". Update the flavor setting.";
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "The search cluster did not respond within " + _TestConnectionTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            ObservabilitySettingsSupport.AddCommonSettings(schema, "Cluster URL, for example https://es.example.com:9200 or an OpenSearch endpoint.");
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.Flavor, SettingValueKindEnum.Enum, "Flavor")
            {
                DefaultValue = "Elasticsearch",
                AllowedValues = Enum.GetNames(typeof(ElasticsearchFlavorEnum)).ToList(),
                Group = "Connection",
                ShowInSummary = true,
                Help = "Elasticsearch or OpenSearch. Test Connection reports the detected distribution."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.AllowedIndexPatterns, SettingValueKindEnum.StringList, "Allowed index patterns")
            {
                Group = "Safety",
                ShowInSummary = true,
                Help = "Index globs calls may read, one per line (for example logs-* and metrics-2026.*). Empty allows every index whose name does not start with a dot. Hidden (dot) indexes need a pattern that starts with a dot."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxHits, SettingValueKindEnum.Integer, "Maximum hits")
            {
                DefaultValue = ElasticsearchSettings.DefaultMaxHits.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 10000,
                Group = "Limits",
                Help = "Most hits one es_search returns; larger sizes are lowered. Default 100."
            });

            return new ConnectorDescriptor
            {
                Type = TypeName,
                Family = FamilyName,
                DisplayName = "Elasticsearch / OpenSearch",
                Description = "Search and count documents with the Query DSL in allowed indexes, and inspect cluster health, indexes, and field mappings. Works with Elasticsearch and OpenSearch. Read-only.",
                Category = ConnectorCategoryEnum.Observability,
                IconKey = "search",
                Version = "1.0.0",
                SettingsSchema = schema,
                SecretSlots = ObservabilitySettingsSupport.SecretSlots(),
                DefaultInstructionsKey = "connector." + TypeName + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(ElasticsearchConnector).Assembly, "connector.elasticsearch.instructions.md"),
                DocumentationUrl = "https://www.elastic.co/docs/reference/elasticsearch/rest-apis"
            };
        }

        #endregion
    }
}
