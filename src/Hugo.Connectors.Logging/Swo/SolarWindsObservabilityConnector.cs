namespace Hugo.Connectors.Logging
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
    /// SolarWinds Observability connector (type <c>solarwinds-observability</c>, family <c>logs</c>): log search and
    /// tail, entities, and metrics through the public REST API. Every tool is Read. The public API has no alert
    /// listing operation, so <c>swo_list_alerts</c> is not offered. Thread-safe.
    /// </summary>
    public class SolarWindsObservabilityConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "solarwinds-observability";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Options shared with the tools. Never null.
        /// </summary>
        public LoggingConnectorOptions Options { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with default options.
        /// </summary>
        public SolarWindsObservabilityConnector() : this(new LoggingConnectorOptions())
        {
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public SolarWindsObservabilityConnector(LoggingConnectorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new SwoSearchTool(Options),
                new SwoTailTool(Options),
                new SwoListEntitiesTool(Options),
                new SwoGetEntityTool(Options),
                new SwoQueryMetricTool(Options)
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
            string? baseUrl = resource.Settings.GetString("baseUrl");
            string? dataCenter = resource.Settings.GetString("dataCenter");
            if (baseUrl != null && !LoggingHttpTransport.IsHttpUrl(baseUrl))
                result.Add("baseUrl", "invalid_format", "baseUrl must be an absolute http or https URL including /v1, for example " + SwoSettings.BaseUrlFor("na-01") + ".");
            if (baseUrl == null && dataCenter != null && !SwoSettings.IsKnownDataCenter(dataCenter))
                result.Add("dataCenter", "invalid_value", "dataCenter must be one of " + String.Join(", ", SwoSettings.DataCenters) + ", or set baseUrl.");
            ValidateRange(resource, result, "maxEvents", 1, 10000);
            ValidateRange(resource, result, "maxTimeRangeHours", 1, 8760);
            if (!ApiTokenSlot.IsPresent(resource))
                result.Add(ApiTokenSlot.Name, "required", "Bind an ApiToken secret holding a SolarWinds Observability API token (Settings, API Tokens; ingestion tokens do not work).");
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch sw = Stopwatch.StartNew();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(Options.TestConnectionTimeout);
                try
                {
                    SwoClient client = SwoClient.FromResource(resource);
                    SwoEntitiesResponse response = await client.ListEntitiesAsync(null, null, null, 1, null, timeout.Token).ConfigureAwait(false);
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok("Connected to SolarWinds Observability at " + client.Settings.BaseUrl + ".", sw.Elapsed.TotalMilliseconds);
                    ok.Details["baseUrl"] = client.Settings.BaseUrl;
                    ok.Details["entitiesVisible"] = response.Entities!.Any() ? "yes" : "no";
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail("timeout", "SolarWinds Observability did not respond within " + Options.TestConnectionTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
                }
                catch (ConnectorException e)
                {
                    return ConnectorHealthResult.Fail(e.Code, e.Message, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static void ValidateRange(ConnectorResourceContext resource, ConnectorValidationResult result, string key, long min, long max)
        {
            string? raw = resource.Settings.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                result.Add(key, "out_of_range", key + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor("dataCenter", SettingValueKindEnum.Enum, "Data center")
            {
                Help = "Region of the SolarWinds Observability organization. The API host is api.<dataCenter>.cloud.solarwinds.com.",
                DefaultValue = SwoSettings.DefaultDataCenter,
                AllowedValues = SwoSettings.DataCenters.ToList(),
                Group = "Connection",
                ShowInSummary = true
            });
            schema.Add(new SettingDescriptor("baseUrl", SettingValueKindEnum.String, "API base URL override")
            {
                Help = "Full API base URL including /v1. Overrides dataCenter; use only for a proxy or a test stub.",
                Pattern = "^https?://",
                Group = "Connection"
            });
            schema.Add(new SettingDescriptor("defaultGroup", SettingValueKindEnum.String, "Default log group")
            {
                Help = "Log group searched when a call names none. Empty searches every group.",
                Group = "Search"
            });
            schema.Add(new SettingDescriptor("maxEvents", SettingValueKindEnum.Integer, "Maximum events per call")
            {
                Help = "Upper bound on events returned by one logs_search or logs_tail call.",
                DefaultValue = SwoSettings.DefaultMaxEvents.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 10000,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor("maxTimeRangeHours", SettingValueKindEnum.Integer, "Maximum time range (hours)")
            {
                Help = "Longest window one log search or metric query may cover. Default 168 (one week).",
                DefaultValue = SwoSettings.DefaultMaxTimeRangeHours.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 8760,
                Group = "Limits"
            });

            return new ConnectorDescriptor
            {
                Type = TypeName,
                Family = PapertrailConnector.FamilyName,
                DisplayName = "SolarWinds Observability",
                Description = "Search and tail logs, inspect entities, and query metrics in SolarWinds Observability SaaS through its public REST API.",
                Category = ConnectorCategoryEnum.Logging,
                IconKey = "logs",
                Version = "1.0.0",
                SettingsSchema = schema,
                SecretSlots = new List<SecretSlotDescriptor>
                {
                    ApiTokenSlot.Descriptor("SolarWinds Observability API token (Settings, API Tokens). The public API requires a full-access token; ingestion tokens are rejected.")
                },
                DefaultInstructionsKey = "connector." + TypeName + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(SolarWindsObservabilityConnector).Assembly, "connector.solarwinds-observability.instructions.md"),
                DocumentationUrl = "https://documentation.solarwinds.com/en/success_center/observability/content/api/api-swagger.htm"
            };
        }

        #endregion
    }
}
