namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Papertrail log search connector (type <c>papertrail</c>, family <c>logs</c>). Every tool is Read. Thread-safe.
    /// </summary>
    public class PapertrailConnector : IConnector
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public static readonly string TypeName = "papertrail";

        /// <summary>
        /// Tool family shared with other log search connectors.
        /// </summary>
        public static readonly string FamilyName = "logs";

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
        public PapertrailConnector() : this(new LoggingConnectorOptions())
        {
        }

        /// <summary>
        /// Instantiate with options.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailConnector(LoggingConnectorOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Descriptor = BuildDescriptor();
            Tools = new List<IConnectorTool>
            {
                new PapertrailSearchTool(Options),
                new PapertrailTailTool(Options),
                new PapertrailVolumeTool(Options),
                new PapertrailListSystemsTool(Options),
                new PapertrailListGroupsTool(Options),
                new PapertrailListSavedSearchesTool(Options)
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
            if (baseUrl != null && !LoggingHttpTransport.IsHttpUrl(baseUrl))
                result.Add("baseUrl", "invalid_format", "baseUrl must be an absolute http or https URL, for example " + PapertrailSettings.DefaultBaseUrl + ".");
            ValidateRange(resource, result, "maxEvents", 1, 10000);
            ValidateRange(resource, result, "maxTimeRangeHours", 1, 8760);
            if (!ApiTokenSlot.IsPresent(resource))
                result.Add(ApiTokenSlot.Name, "required", "Bind an ApiToken secret holding a Papertrail API token (Settings, Profile, API Token in Papertrail).");
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
                    PapertrailClient client = PapertrailClient.FromResource(resource);
                    List<PapertrailSystem> systems = await client.ListSystemsAsync(timeout.Token).ConfigureAwait(false);
                    ConnectorHealthResult ok = ConnectorHealthResult.Ok("Connected to Papertrail; " + systems.Count.ToString(CultureInfo.InvariantCulture) + " system(s) visible.", sw.Elapsed.TotalMilliseconds);
                    ok.Details["systems"] = systems.Count.ToString(CultureInfo.InvariantCulture);
                    ok.Details["baseUrl"] = client.Settings.BaseUrl;
                    return ok;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    return ConnectorHealthResult.Fail("timeout", "Papertrail did not respond within " + Options.TestConnectionTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds.", sw.Elapsed.TotalMilliseconds);
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
            schema.Add(new SettingDescriptor("baseUrl", SettingValueKindEnum.String, "API base URL")
            {
                Help = "Papertrail API base URL. Change it only for a proxy or a test stub.",
                DefaultValue = PapertrailSettings.DefaultBaseUrl,
                Pattern = "^https?://",
                Group = "Connection"
            });
            schema.Add(new SettingDescriptor("defaultGroup", SettingValueKindEnum.String, "Default group")
            {
                Help = "Group ID or name searched when a call names no group or system. Empty searches every system.",
                Group = "Search",
                ShowInSummary = true
            });
            schema.Add(new SettingDescriptor("defaultSystem", SettingValueKindEnum.String, "Default system")
            {
                Help = "System ID or name searched when a call names no system. Empty searches every system.",
                Group = "Search"
            });
            schema.Add(new SettingDescriptor("maxEvents", SettingValueKindEnum.Integer, "Maximum events per call")
            {
                Help = "Upper bound on events returned by one logs_search or logs_tail call. Papertrail allows at most 10000.",
                DefaultValue = PapertrailSettings.DefaultMaxEvents.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 10000,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor("maxTimeRangeHours", SettingValueKindEnum.Integer, "Maximum time range (hours)")
            {
                Help = "Longest window one search or volume call may cover. Default 168 (one week).",
                DefaultValue = PapertrailSettings.DefaultMaxTimeRangeHours.ToString(CultureInfo.InvariantCulture),
                Minimum = 1,
                Maximum = 8760,
                Group = "Limits"
            });

            return new ConnectorDescriptor
            {
                Type = TypeName,
                Family = FamilyName,
                DisplayName = "Papertrail",
                Description = "Search, tail, and count log events in Papertrail (SolarWinds) with time-bounded queries, and list its systems, groups, and saved searches.",
                Category = ConnectorCategoryEnum.Logging,
                IconKey = "logs",
                Version = "1.0.0",
                SettingsSchema = schema,
                SecretSlots = new List<SecretSlotDescriptor>
                {
                    ApiTokenSlot.Descriptor("Papertrail API token from Settings, Profile. The token has the permissions of its user; a read-only member account is recommended.")
                },
                DefaultInstructionsKey = "connector." + TypeName + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(PapertrailConnector).Assembly, "connector.papertrail.instructions.md"),
                DocumentationUrl = "https://www.papertrail.com/help/http-api/"
            };
        }

        #endregion
    }
}
