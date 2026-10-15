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
    /// Grafana Loki connector (type <c>loki</c>, family <c>loki</c>): LogQL instant and range queries, label names, and
    /// label values, with an optional tenant header. Every tool is Read. Thread-safe.
    /// </summary>
    public class LokiConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "loki";

        /// <summary>
        /// Tool family.
        /// </summary>
        public static readonly string FamilyName = "loki";

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
        public LokiConnector()
        {
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new LokiQueryTool(),
                new LokiQueryRangeTool(),
                new LokiLabelsTool(false),
                new LokiLabelsTool(true)
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
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxLines, 1, 5000);
            ObservabilitySettingsSupport.ValidateRange(resource, result, ObservabilitySettingKeys.MaxRangeHours, 1, 8760);
            string? tenant = resource.Settings.GetString(ObservabilitySettingKeys.TenantHeader);
            if (tenant != null && (tenant.IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0 || tenant.Length > 256))
                result.Add(ObservabilitySettingKeys.TenantHeader, "invalid_format", "tenantHeader must be a single-line tenant ID of at most 256 characters.");
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
                    LokiSettings settings = new LokiSettings(resource.Settings);
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
                    {
                        new KeyValuePair<string, string?>("start", ObservabilityTime.UnixNanoseconds(now.AddHours(-1))),
                        new KeyValuePair<string, string?>("end", ObservabilityTime.UnixNanoseconds(now))
                    };
                    PromResponse<List<string>> labels = await LokiToolBase.Client(resource, settings).GetAsync<List<string>>("loki/api/v1/labels", q, timeout.Token).ConfigureAwait(false);
                    int count = labels.Data!.Count;
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok("Connected to Loki; " + count.ToString(CultureInfo.InvariantCulture) + " label name(s) in the last hour.", sw.Elapsed.TotalMilliseconds);
                    ok.Details["labels"] = count.ToString(CultureInfo.InvariantCulture);
                    ok.Details["baseUrl"] = settings.Endpoint.BaseUrl;
                    ok.Details["tenant"] = settings.TenantId ?? "(none)";
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail(ConnectorErrorCodes.Timeout, "Loki did not respond within " + _TestConnectionTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
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
            ObservabilitySettingsSupport.AddCommonSettings(schema, "Loki URL, for example http://loki:3100 (or the Grafana Cloud Logs URL).");
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.TenantHeader, SettingValueKindEnum.String, "Tenant ID")
            {
                Group = "Connection",
                ShowInSummary = true,
                Help = "Sent as the X-Scope-OrgID header on every request for multi-tenant Loki. Leave empty for single-tenant Loki."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxLines, SettingValueKindEnum.Integer, "Maximum lines")
            {
                DefaultValue = LokiSettings.DefaultMaxLines.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 5000,
                Group = "Limits",
                Help = "Most log lines one call returns. Default 1000, maximum 5000."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxRangeHours, SettingValueKindEnum.Integer, "Maximum range (hours)")
            {
                DefaultValue = LokiSettings.DefaultMaxRangeHours.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 8760,
                Group = "Limits",
                Help = "Longest window one query may cover. Default 24."
            });

            return new ConnectorDescriptor
            {
                Type = TypeName,
                Family = FamilyName,
                DisplayName = "Grafana Loki",
                Description = "Query Grafana Loki logs with LogQL over bounded windows (log lines and metric queries) and discover stream labels. Supports multi-tenant Loki through the X-Scope-OrgID header. Read-only.",
                Category = ConnectorCategoryEnum.Observability,
                IconKey = "logs",
                Version = "1.0.0",
                SettingsSchema = schema,
                SecretSlots = ObservabilitySettingsSupport.SecretSlots(),
                DefaultInstructionsKey = "connector." + TypeName + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(LokiConnector).Assembly, "connector.loki.instructions.md"),
                DocumentationUrl = "https://grafana.com/docs/loki/latest/reference/loki-http-api/"
            };
        }

        #endregion
    }
}
