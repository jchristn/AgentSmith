namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Prometheus connector (type <c>prometheus</c>, family <c>prom</c>): PromQL instant and range queries, alerts,
    /// rules, targets, series, and label values. Every tool is Read. Thread-safe.
    /// </summary>
    public class PrometheusConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "prometheus";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "prom";

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
        public PrometheusConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new PromQueryTool(),
                new PromQueryRangeTool(),
                new PromAlertsTool(),
                new PromRulesTool(),
                new PromTargetsTool(),
                new PromSeriesTool(),
                new PromLabelValuesTool()
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
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxSeries, 1, 10000);
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxPointsPerSeries, 10, 11000);
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxRangeHours, 1, 8760);
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
                    PrometheusSettings settings = new PrometheusSettings(resource.Settings);
                    PromApiClient client = new PromApiClient(new ObservabilityTransport(settings.Endpoint, PromToolBase.ProviderName, resource.Secrets, null, resource.Logger));
                    PromResponse<PromBuildInfo> info = await client.GetAsync<PromBuildInfo>("api/v1/status/buildinfo", null, timeout.Token).ConfigureAwait(false);
                    string version = info.Data!.Version ?? "unknown";
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok("Connected to Prometheus " + version + ".", sw.Elapsed.TotalMilliseconds);
                    ok.Details["version"] = version;
                    ok.Details["baseUrl"] = settings.Endpoint.BaseUrl;
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "Prometheus did not respond within " + _TestConnectionTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
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
            ObservabilitySettingsSupport.AddCommonSettings(schema, "Prometheus server URL, for example http://prometheus:9090. Include a path prefix if Prometheus runs behind one.");
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxSeries, SettingValueKindEnum.Integer, "Maximum series")
            {
                DefaultValue = PrometheusSettings.DefaultMaxSeries.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 10000,
                Group = "Limits",
                Help = "Most series, targets, or values one call returns; the rest are dropped and reported. Default 500."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxPointsPerSeries, SettingValueKindEnum.Integer, "Maximum points per series")
            {
                DefaultValue = PrometheusSettings.DefaultMaxPointsPerSeries.ToString(CultureInfo.InvariantCulture),
                Minimum = 10,
                Maximum = 11000,
                Group = "Limits",
                Help = "Most samples per series in prom_query_range. The step is raised automatically to stay within it. Default 240."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxRangeHours, SettingValueKindEnum.Integer, "Maximum range (hours)")
            {
                DefaultValue = PrometheusSettings.DefaultMaxRangeHours.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 8760,
                Group = "Limits",
                Help = "Longest window one range, series, or label call may cover. Default 168 (one week)."
            });

            return new ConnectorDescriptor
            {
                Type = TypeName,
                Family = FamilyName,
                DisplayName = "Prometheus",
                Description = "Query Prometheus metrics with PromQL (instant and range, with bounded windows and steps) and inspect alerts, rules, scrape targets, series, and labels. Read-only.",
                Category = ConnectorCategoryEnum.Observability,
                IconKey = "chart",
                Version = "1.0.0",
                SettingsSchema = schema,
                SecretSlots = ObservabilitySettingsSupport.SecretSlots(),
                DefaultInstructionsKey = "connector." + TypeName + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(PrometheusConnector).Assembly, "connector.prometheus.instructions.md"),
                DocumentationUrl = "https://prometheus.io/docs/prometheus/latest/querying/api/"
            };
        }

        #endregion
    }
}
