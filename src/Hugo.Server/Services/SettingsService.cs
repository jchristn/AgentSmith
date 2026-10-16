namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Services;
    using Hugo.Server.Settings;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Settings API backend (plan D-014). Settings-file fields are read from the live settings, validated, written to
    /// the settings file (never writing environment-overridden values), and applied live when safe. Cluster runtime
    /// fields are stored through <see cref="ClusterSettingsService"/>. Thread-safe for concurrent reads; updates are
    /// serialized.
    /// </summary>
    public class SettingsService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Mask returned in place of secret values; submitting it leaves the secret unchanged.
        /// </summary>
        public static readonly string SecretMask = "********";

        /// <summary>
        /// Whether a restart-required change has been saved since this process started.
        /// </summary>
        public bool RestartPending
        {
            get
            {
                return _RestartPending;
            }
        }

        #endregion

        #region Private-Members

        private readonly HugoSettings _Settings;
        private readonly ClusterSettingsService _Cluster;
        private readonly HugoLog _Logging;
        private readonly List<FileSettingDescriptor> _Fields;
        private readonly SemaphoreSlim _UpdateLock = new SemaphoreSlim(1, 1);
        private bool _RestartPending = false;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Live settings.</param>
        /// <param name="cluster">Cluster settings service.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SettingsService(HugoSettings settings, ClusterSettingsService cluster, HugoLog logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Fields = BuildFields();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the settings file can be written.
        /// </summary>
        /// <returns>True when writable.</returns>
        public bool FileWritable()
        {
            return !_Settings.SettingsFileReadOnly && !String.IsNullOrEmpty(_Settings.SourceFile);
        }

        /// <summary>
        /// Build the settings view.
        /// </summary>
        /// <returns>View.</returns>
        public SettingsView View()
        {
            SettingsView view = new SettingsView();
            view.SettingsFile = _Settings.SourceFile;
            view.SettingsFileWritable = FileWritable();
            view.RestartPending = _RestartPending;

            foreach (FileSettingDescriptor field in _Fields)
            {
                SettingField f = new SettingField();
                f.Path = field.Path;
                f.Group = field.Path.Split('.')[0];
                f.Description = field.Description;
                f.Type = field.Type;
                f.IsSecret = field.IsSecret;
                string? value = field.Get(_Settings);
                if (field.IsSecret)
                {
                    f.IsSet = !String.IsNullOrEmpty(value);
                    f.Value = f.IsSet ? SecretMask : null;
                }
                else
                {
                    f.Value = value;
                    f.IsSet = value != null;
                }
                if (_Settings.EnvironmentOverrides.Contains(field.Path)) f.Source = "Environment";
                else if (!FileWritable()) f.Source = "ConfigMap";
                else f.Source = "File";
                f.Editable = f.Source == "File";
                f.ApplyMode = field.Live ? "Live" : "RestartRequired";
                f.Minimum = field.Minimum;
                f.Maximum = field.Maximum;
                f.AllowedValues = field.AllowedValues;
                view.Fields.Add(f);
            }

            foreach (ClusterSettingDefinition definition in _Cluster.Definitions)
            {
                SettingField f = new SettingField();
                f.Path = definition.Key;
                f.Group = definition.Key.Split('.')[0];
                f.Description = definition.Description;
                f.Type = definition.Type;
                f.Value = _Cluster.GetString(definition.Key);
                f.IsSet = definition.Type != "string" || !String.IsNullOrEmpty(f.Value);
                f.Source = _Cluster.IsOverridden(definition.Key) ? "Environment" : "Database";
                f.ApplyMode = "Live";
                f.Editable = f.Source == "Database";
                if (definition.Type == "int")
                {
                    f.Minimum = definition.Minimum;
                    f.Maximum = definition.Maximum;
                }
                view.Fields.Add(f);
            }

            return view;
        }

        /// <summary>
        /// Validate and apply an update.
        /// </summary>
        /// <param name="request">Update request.</param>
        /// <param name="principal">Principal making the change.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Save result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ValidationException">Thrown with per-field errors when any value is invalid.</exception>
        public async Task<SettingsSaveResponse> UpdateAsync(SettingsUpdateRequest request, string? principal, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            await _UpdateLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                Dictionary<string, string> codes = new Dictionary<string, string>();
                List<FileSettingDescriptor> fileChanges = new List<FileSettingDescriptor>();
                List<SettingValue> fileValues = new List<SettingValue>();
                List<SettingValue> clusterValues = new List<SettingValue>();

                foreach (SettingValue item in request.Values)
                {
                    if (item == null || String.IsNullOrWhiteSpace(item.Path))
                    {
                        errors["values"] = "Every value needs a setting path.";
                        codes["values"] = "PathRequired";
                        continue;
                    }
                    FileSettingDescriptor? field = _Fields.FirstOrDefault(f => String.Equals(f.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                    if (field != null)
                    {
                        if (field.IsSecret && (String.IsNullOrEmpty(item.Value) || item.Value == SecretMask)) continue;
                        if (_Settings.EnvironmentOverrides.Contains(field.Path))
                        {
                            errors[item.Path] = "Overridden by an environment variable; change the environment instead.";
                            codes[item.Path] = "OverriddenByEnvironment";
                            continue;
                        }
                        if (!FileWritable())
                        {
                            errors[item.Path] = "The settings file is managed externally (for example a ConfigMap) and is read-only.";
                            codes[item.Path] = "SettingsFileReadOnly";
                            continue;
                        }
                        string? error = Validate(field, item.Value, out string code);
                        if (error != null)
                        {
                            errors[item.Path] = error;
                            codes[item.Path] = code;
                        }
                        else
                        {
                            fileChanges.Add(field);
                            fileValues.Add(item);
                        }
                        continue;
                    }

                    if (_Cluster.Find(item.Path) != null)
                    {
                        if (_Cluster.IsOverridden(item.Path))
                        {
                            errors[item.Path] = "Overridden by an environment variable; change the environment instead.";
                            codes[item.Path] = "OverriddenByEnvironment";
                        }
                        else if (_Cluster.Normalize(item.Path, item.Value, out string? clusterError, out string clusterCode) == null)
                        {
                            errors[item.Path] = clusterError ?? "Invalid value.";
                            codes[item.Path] = clusterCode;
                        }
                        else clusterValues.Add(item);
                        continue;
                    }

                    errors[item.Path] = "Unknown setting.";
                    codes[item.Path] = "UnknownSetting";
                }

                // Nothing is saved, and nothing is clamped, when any value is invalid.
                if (errors.Count > 0) throw new ValidationException("One or more settings are invalid. Nothing was saved.", errors, codes);

                SettingsSaveResponse response = new SettingsSaveResponse();

                if (fileChanges.Count > 0)
                {
                    HugoSettings fileCopy = File.Exists(_Settings.SourceFile!) ? HugoSettings.FromFile(_Settings.SourceFile!) : new HugoSettings();
                    for (int i = 0; i < fileChanges.Count; i++) fileChanges[i].Set(fileCopy, fileValues[i].Value);
                    fileCopy.ToFile(_Settings.SourceFile!);

                    for (int i = 0; i < fileChanges.Count; i++)
                    {
                        if (fileChanges[i].Live)
                        {
                            fileChanges[i].Set(_Settings, fileValues[i].Value);
                            response.AppliedLive.Add(fileChanges[i].Path);
                            if (fileChanges[i].Path == "Logging.MinimumSeverity") _Logging.Settings.MinimumSeverity = (Severity)_Settings.Logging.MinimumSeverity;
                        }
                        else
                        {
                            response.PendingRestart.Add(fileChanges[i].Path);
                            _RestartPending = true;
                        }
                    }
                }

                foreach (SettingValue item in clusterValues)
                {
                    await _Cluster.SetAsync(item.Path, item.Value, principal, token).ConfigureAwait(false);
                    response.AppliedLive.Add(item.Path);
                }

                _Logging.Info("[Settings] " + (principal ?? "unknown") + " changed " + String.Join(", ", response.AppliedLive.Concat(response.PendingRestart)));
                response.Saved = true;
                response.RestartRequired = response.PendingRestart.Count > 0;
                response.Settings = View();
                return response;
            }
            finally
            {
                _UpdateLock.Release();
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _UpdateLock.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static string? Validate(FileSettingDescriptor field, string? value, out string code)
        {
            code = String.Empty;
            switch (field.Type)
            {
                case "int":
                    if (!Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    {
                        code = "NotAnInteger";
                        return "Must be an integer.";
                    }
                    if ((field.Minimum.HasValue && n < field.Minimum.Value) || (field.Maximum.HasValue && n > field.Maximum.Value))
                    {
                        code = "OutOfRange";
                        return RangeMessage(field.Minimum, field.Maximum);
                    }
                    return null;
                case "bool":
                    if (Boolean.TryParse(value, out bool _)) return null;
                    code = "NotABoolean";
                    return "Must be true or false.";
                case "enum":
                    if (field.AllowedValues != null && field.AllowedValues.Any(a => String.Equals(a, value, StringComparison.OrdinalIgnoreCase))) return null;
                    code = "NotAllowedValue";
                    return "Must be one of " + String.Join(", ", field.AllowedValues ?? new List<string>()) + ".";
                default:
                    if (value == null || value.Length <= 4096) return null;
                    code = "TooLong";
                    return "Must be at most 4096 characters.";
            }
        }

        private static string RangeMessage(long? minimum, long? maximum)
        {
            if (minimum.HasValue && maximum.HasValue) return "Must be between " + minimum.Value + " and " + maximum.Value + ".";
            if (minimum.HasValue) return "Must be at least " + minimum.Value + ".";
            return "Must be at most " + maximum!.Value + ".";
        }

        private static int ParseInt(string? value)
        {
            return Int32.Parse(value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string? value)
        {
            return Boolean.Parse(value ?? "false");
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static FileSettingDescriptor Field(string path, string type, bool live, string description, Func<HugoSettings, string?> get, Action<HugoSettings, string?> set, long? min, long? max, List<string>? allowed)
        {
            FileSettingDescriptor d = new FileSettingDescriptor();
            d.Path = path;
            d.Type = type;
            d.IsSecret = type == "secret";
            d.Live = live;
            d.Description = description;
            d.Get = get;
            d.Set = set;
            d.Minimum = min;
            d.Maximum = max;
            d.AllowedValues = allowed;
            return d;
        }

        private static List<FileSettingDescriptor> BuildFields()
        {
            List<FileSettingDescriptor> list = new List<FileSettingDescriptor>();
            list.Add(Field("Rest.Hostname", "string", false, "REST bind hostname (127.0.0.1 locally, * or 0.0.0.0 in a container).", s => s.Rest.Hostname, (s, v) => s.Rest.Hostname = v ?? "127.0.0.1", null, null, null));
            list.Add(Field("Rest.Port", "int", false, "REST port.", s => s.Rest.Port.ToString(CultureInfo.InvariantCulture), (s, v) => s.Rest.Port = ParseInt(v), 1L, 65535L, null));
            list.Add(Field("Rest.Ssl", "bool", false, "Whether TLS is enabled on the REST listener.", s => Bool(s.Rest.Ssl), (s, v) => s.Rest.Ssl = ParseBool(v), null, null, null));
            list.Add(Field("Rest.DatabaseRetryAfterSeconds", "int", true, "Retry-After seconds on 503 DatabaseBusy and DatabaseUnavailable responses.", s => s.Rest.DatabaseRetryAfterSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Rest.DatabaseRetryAfterSeconds = ParseInt(v), 1L, 300L, null));
            list.Add(Field("Rest.ExternalBaseUrl", "string", true, "External REST base URL used in A2A agent cards and server info, for example https://hugo.example.com. Empty uses each request's scheme and Host header.", s => s.Rest.ExternalBaseUrl, (s, v) => s.Rest.ExternalBaseUrl = Empty(v), null, null, null));
            list.Add(Field("Database.Type", "enum", false, "Database provider.", s => s.Database.Type.ToString(), (s, v) => s.Database.Type = Enum.Parse<DatabaseTypeEnum>(v ?? "Sqlite", true), null, null, new List<string>(Enum.GetNames(typeof(DatabaseTypeEnum)))));
            list.Add(Field("Database.Filename", "string", false, "Sqlite database file.", s => s.Database.Filename, (s, v) => s.Database.Filename = v ?? String.Empty, null, null, null));
            list.Add(Field("Database.Host", "string", false, "Postgresql host.", s => s.Database.Host, (s, v) => s.Database.Host = v ?? String.Empty, null, null, null));
            list.Add(Field("Database.Port", "int", false, "Postgresql port.", s => s.Database.Port.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.Port = ParseInt(v), 1L, 65535L, null));
            list.Add(Field("Database.DatabaseName", "string", false, "Database name.", s => s.Database.DatabaseName, (s, v) => s.Database.DatabaseName = v ?? String.Empty, null, null, null));
            list.Add(Field("Database.Username", "string", false, "Database username.", s => s.Database.Username, (s, v) => s.Database.Username = v ?? String.Empty, null, null, null));
            list.Add(Field("Database.Password", "secret", false, "Database password.", s => s.Database.Password, (s, v) => s.Database.Password = v, null, null, null));
            list.Add(Field("Database.Schema", "string", false, "Postgresql schema.", s => s.Database.Schema, (s, v) => s.Database.Schema = Empty(v), null, null, null));
            list.Add(Field("Database.MaxPoolSize", "int", false, "Maximum pooled connections per replica. Replicas times this must stay below the Postgresql max_connections.", s => s.Database.MaxPoolSize.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.MaxPoolSize = ParseInt(v), 1L, 1000L, null));
            list.Add(Field("Database.MinPoolSize", "int", false, "Connections each replica keeps open while idle (Postgresql).", s => s.Database.MinPoolSize.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.MinPoolSize = ParseInt(v), 0L, 1000L, null));
            list.Add(Field("Database.ConnectionTimeoutSeconds", "int", false, "Seconds to wait for a pooled connection before failing (Postgresql).", s => s.Database.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.ConnectionTimeoutSeconds = ParseInt(v), 1L, 600L, null));
            list.Add(Field("Database.CommandTimeoutSeconds", "int", false, "Seconds one database command may run, 0 for no limit (Postgresql).", s => s.Database.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.CommandTimeoutSeconds = ParseInt(v), 0L, 3600L, null));
            list.Add(Field("Database.ConnectionIdleLifetimeSeconds", "int", false, "Seconds an idle pooled connection is kept before it is closed (Postgresql).", s => s.Database.ConnectionIdleLifetimeSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Database.ConnectionIdleLifetimeSeconds = ParseInt(v), 1L, 3600L, null));
            list.Add(Field("Logging.ConsoleLogging", "bool", false, "Log to the console.", s => Bool(s.Logging.ConsoleLogging), (s, v) => s.Logging.ConsoleLogging = ParseBool(v), null, null, null));
            list.Add(Field("Logging.FileLogging", "bool", false, "Log to a file.", s => Bool(s.Logging.FileLogging), (s, v) => s.Logging.FileLogging = ParseBool(v), null, null, null));
            list.Add(Field("Logging.LogDirectory", "string", false, "Log directory.", s => s.Logging.LogDirectory, (s, v) => s.Logging.LogDirectory = v ?? "logs/", null, null, null));
            list.Add(Field("Logging.LogFilename", "string", false, "Log file name.", s => s.Logging.LogFilename, (s, v) => s.Logging.LogFilename = v ?? "hugo.log", null, null, null));
            list.Add(Field("Logging.MinimumSeverity", "int", true, "Minimum severity, 0 (debug) to 7 (emergency).", s => s.Logging.MinimumSeverity.ToString(CultureInfo.InvariantCulture), (s, v) => s.Logging.MinimumSeverity = ParseInt(v), 0L, 7L, null));
            list.Add(Field("Auth.Issuer", "string", false, "Token issuer.", s => s.Auth.Issuer, (s, v) => s.Auth.Issuer = v ?? "hugo", null, null, null));
            list.Add(Field("Auth.SigningKey", "secret", false, "Legacy session token validation key; new tokens use the master key.", s => s.Auth.SigningKey, (s, v) => s.Auth.SigningKey = v ?? String.Empty, null, null, null));
            list.Add(Field("Auth.AdminApiKey", "secret", true, "System administrator API key.", s => s.Auth.AdminApiKey, (s, v) => s.Auth.AdminApiKey = v ?? String.Empty, null, null, null));
            list.Add(Field("Auth.SignedRequestSkewSeconds", "int", true, "Allowed clock skew for signed requests.", s => s.Auth.SignedRequestSkewSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.SignedRequestSkewSeconds = ParseInt(v), 10L, 3600L, null));
            list.Add(Field("Auth.RateLimitPerMinute", "int", true, "Authentication attempts per source IP per minute, counted across all replicas.", s => s.Auth.RateLimitPerMinute.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.RateLimitPerMinute = ParseInt(v), 1L, 100000L, null));
            list.Add(Field("Auth.PasswordHashIterations", "int", true, "PBKDF2-HMAC-SHA256 iterations for new password hashes; older hashes are upgraded at sign-in.", s => s.Auth.PasswordHashIterations.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.PasswordHashIterations = ParseInt(v), 600000L, 10000000L, null));
            list.Add(Field("Auth.UserSessionMinutes", "int", true, "Session lifetime for tenant users in minutes.", s => s.Auth.UserSessionMinutes.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.UserSessionMinutes = ParseInt(v), 1L, 10080L, null));
            list.Add(Field("Auth.AdminSessionMinutes", "int", true, "Session lifetime for system administrators in minutes.", s => s.Auth.AdminSessionMinutes.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.AdminSessionMinutes = ParseInt(v), 1L, 10080L, null));
            list.Add(Field("Auth.SessionAbsoluteMaxHours", "int", true, "Hours after sign-in beyond which a session can no longer be refreshed.", s => s.Auth.SessionAbsoluteMaxHours.ToString(CultureInfo.InvariantCulture), (s, v) => s.Auth.SessionAbsoluteMaxHours = ParseInt(v), 1L, 720L, null));
            list.Add(Field("RequestHistory.MaxRequestBodyBytes", "int", true, "Maximum captured request body bytes.", s => s.RequestHistory.MaxRequestBodyBytes.ToString(CultureInfo.InvariantCulture), (s, v) => s.RequestHistory.MaxRequestBodyBytes = ParseInt(v), 0L, 1048576L, null));
            list.Add(Field("RequestHistory.MaxResponseBodyBytes", "int", true, "Maximum captured response body bytes.", s => s.RequestHistory.MaxResponseBodyBytes.ToString(CultureInfo.InvariantCulture), (s, v) => s.RequestHistory.MaxResponseBodyBytes = ParseInt(v), 0L, 1048576L, null));
            list.Add(Field("Telemetry.Enabled", "bool", false, "Whether telemetry is enabled.", s => Bool(s.Telemetry.Enabled), (s, v) => s.Telemetry.Enabled = ParseBool(v), null, null, null));
            list.Add(Field("Logging.MaximumKnownSecretCharacters", "int", false, "Maximum retained secret variant characters; overflow fails closed.", s => s.Logging.MaximumKnownSecretCharacters.ToString(CultureInfo.InvariantCulture), (s, v) => s.Logging.MaximumKnownSecretCharacters = ParseInt(v), 1024L, 33554432L, null));
            list.Add(Field("Logging.MaximumKnownSecretValues", "int", false, "Maximum known secret variants retained; overflow fails closed instead of evicting secrets.", s => s.Logging.MaximumKnownSecretValues.ToString(CultureInfo.InvariantCulture), (s, v) => s.Logging.MaximumKnownSecretValues = ParseInt(v), 100L, 1000000L, null));
            list.Add(Field("Telemetry.LogsEnable", "bool", false, "Whether scrubbed logs enter Radiant exporters.", s => Bool(s.Telemetry.LogsEnable), (s, v) => s.Telemetry.LogsEnable = ParseBool(v), null, null, null));
            list.Add(Field("Telemetry.LokiEnable", "bool", false, "Whether scrubbed logs also export directly to Loki OTLP-HTTP.", s => Bool(s.Telemetry.LokiEnable), (s, v) => s.Telemetry.LokiEnable = ParseBool(v), null, null, null));
            list.Add(Field("Telemetry.LokiEndpoint", "string", false, "Loki OTLP base endpoint; /v1/logs is appended.", s => s.Telemetry.LokiEndpoint, (s, v) => s.Telemetry.LokiEndpoint = v ?? String.Empty, null, null, null));
            list.Add(Field("Telemetry.LokiTenantId", "string", false, "Optional Loki tenant header.", s => s.Telemetry.LokiTenantId, (s, v) => s.Telemetry.LokiTenantId = v ?? String.Empty, null, null, null));
            list.Add(Field("Telemetry.LogQueueCapacity", "int", false, "Bounded nonblocking log bridge capacity; excess entries are dropped.", s => s.Telemetry.LogQueueCapacity.ToString(CultureInfo.InvariantCulture), (s, v) => s.Telemetry.LogQueueCapacity = ParseInt(v), 1L, 65536L, null));
            list.Add(Field("Telemetry.LogMaxCharacters", "int", false, "Maximum exported characters in a message or structured details.", s => s.Telemetry.LogMaxCharacters.ToString(CultureInfo.InvariantCulture), (s, v) => s.Telemetry.LogMaxCharacters = ParseInt(v), 256L, 1048576L, null));
            list.Add(Field("Telemetry.ExportTimeoutMs", "int", false, "Per-export timeout in milliseconds.", s => s.Telemetry.ExportTimeoutMs.ToString(CultureInfo.InvariantCulture), (s, v) => s.Telemetry.ExportTimeoutMs = ParseInt(v), 1000L, 30000L, null));
            list.Add(Field("Telemetry.LogDrainTimeoutMs", "int", false, "Log bridge drain timeout at shutdown in milliseconds.", s => s.Telemetry.LogDrainTimeoutMs.ToString(CultureInfo.InvariantCulture), (s, v) => s.Telemetry.LogDrainTimeoutMs = ParseInt(v), 100L, 30000L, null));
            list.Add(Field("Telemetry.ServiceName", "string", false, "Telemetry service name.", s => s.Telemetry.ServiceName, (s, v) => s.Telemetry.ServiceName = v ?? "hugo-server", null, null, null));
            list.Add(Field("Telemetry.PrometheusEnable", "bool", false, "Whether the Prometheus listener is enabled.", s => Bool(s.Telemetry.PrometheusEnable), (s, v) => s.Telemetry.PrometheusEnable = ParseBool(v), null, null, null));
            list.Add(Field("Telemetry.PrometheusPort", "int", false, "Prometheus listener port.", s => s.Telemetry.PrometheusPort.ToString(CultureInfo.InvariantCulture), (s, v) => s.Telemetry.PrometheusPort = ParseInt(v), 1L, 65535L, null));
            list.Add(Field("Telemetry.OtlpEnable", "bool", false, "Whether OTLP export is enabled.", s => Bool(s.Telemetry.OtlpEnable), (s, v) => s.Telemetry.OtlpEnable = ParseBool(v), null, null, null));
            list.Add(Field("Telemetry.OtlpEndpoint", "string", false, "OTLP endpoint.", s => s.Telemetry.OtlpEndpoint, (s, v) => s.Telemetry.OtlpEndpoint = v ?? String.Empty, null, null, null));
            list.Add(Field("Telemetry.ServeMetricsOnServicePort", "bool", true, "Whether GET /metrics is also served on the REST port without authentication, relayed from the Prometheus listener, so kubectl port-forward and in-pod 127.0.0.1 scrapes work.", s => Bool(s.Telemetry.ServeMetricsOnServicePort), (s, v) => s.Telemetry.ServeMetricsOnServicePort = ParseBool(v), null, null, null));
            list.Add(Field("Secrets.MasterKey", "secret", false, "Development-only secrets master key (base64, 32 bytes or more). Use HUGO_MASTER_KEY in production.", s => s.Secrets.MasterKey, (s, v) => s.Secrets.MasterKey = Empty(v), null, null, null));
            list.Add(Field("Secrets.MasterKeyFile", "string", false, "Development-only master key file, created with a random key when no other key is configured. Empty disables it.", s => s.Secrets.MasterKeyFile, (s, v) => s.Secrets.MasterKeyFile = Empty(v), null, null, null));
            list.Add(Field("Secrets.MasterKeyVersion", "int", false, "Version of the current master key.", s => s.Secrets.MasterKeyVersion.ToString(CultureInfo.InvariantCulture), (s, v) => s.Secrets.MasterKeyVersion = ParseInt(v), 1L, 255L, null));
            list.Add(Field("Secrets.PreviousMasterKeyVersion", "int", false, "Version of HUGO_MASTER_KEY_PREVIOUS; 0 means MasterKeyVersion minus 1.", s => s.Secrets.PreviousMasterKeyVersion.ToString(CultureInfo.InvariantCulture), (s, v) => s.Secrets.PreviousMasterKeyVersion = ParseInt(v), 0L, 255L, null));
            list.Add(Field("Secrets.RevealReauthMinutes", "int", true, "Minutes after re-authentication during which a session may reveal secrets.", s => s.Secrets.RevealReauthMinutes.ToString(CultureInfo.InvariantCulture), (s, v) => s.Secrets.RevealReauthMinutes = ParseInt(v), 1L, 60L, null));
            list.Add(Field("Secrets.RevealRateLimitPerMinute", "int", true, "Secret reveals per source IP and per principal per minute.", s => s.Secrets.RevealRateLimitPerMinute.ToString(CultureInfo.InvariantCulture), (s, v) => s.Secrets.RevealRateLimitPerMinute = ParseInt(v), 1L, 1000L, null));
            list.Add(Field("Secrets.RotationBatchSize", "int", true, "Rows re-encrypted per transaction by master key rotation.", s => s.Secrets.RotationBatchSize.ToString(CultureInfo.InvariantCulture), (s, v) => s.Secrets.RotationBatchSize = ParseInt(v), 1L, 5000L, null));
            list.Add(Field("Agent.DefaultMaxIterations", "int", true, "Maximum agent loop iterations per turn for new agent profiles (a profile's own value wins).", s => s.Agent.DefaultMaxIterations.ToString(CultureInfo.InvariantCulture), (s, v) => s.Agent.DefaultMaxIterations = ParseInt(v), 1L, 200L, null));
            list.Add(Field("Agent.DefaultMaxToolCallsPerTurn", "int", true, "Maximum tool calls per turn for new agent profiles (a profile's own value wins).", s => s.Agent.DefaultMaxToolCallsPerTurn.ToString(CultureInfo.InvariantCulture), (s, v) => s.Agent.DefaultMaxToolCallsPerTurn = ParseInt(v), 1L, 500L, null));
            list.Add(Field("Agent.MaxParallelToolCalls", "int", true, "Maximum tool calls run in parallel.", s => s.Agent.MaxParallelToolCalls.ToString(CultureInfo.InvariantCulture), (s, v) => s.Agent.MaxParallelToolCalls = ParseInt(v), 1L, 64L, null));
            list.Add(Field("Agent.ToolRetrievalThreshold", "int", true, "Tool count above which tools are retrieved by relevance.", s => s.Agent.ToolRetrievalThreshold.ToString(CultureInfo.InvariantCulture), (s, v) => s.Agent.ToolRetrievalThreshold = ParseInt(v), 1L, 10000L, null));
            list.Add(Field("Approvals.ExpirySeconds", "int", true, "Seconds before a pending approval expires.", s => s.Approvals.ExpirySeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Approvals.ExpirySeconds = ParseInt(v), 30L, 604800L, null));
            list.Add(Field("Approvals.McpWaitSeconds", "int", true, "Seconds an MCP tool call waits for an approval.", s => s.Approvals.McpWaitSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Approvals.McpWaitSeconds = ParseInt(v), 0L, 600L, null));
            list.Add(Field("Approvals.AllowSelfApproval", "bool", true, "Whether a requester may approve their own request.", s => Bool(s.Approvals.AllowSelfApproval), (s, v) => s.Approvals.AllowSelfApproval = ParseBool(v), null, null, null));
            list.Add(Field("Connectors.EnablePlugins", "bool", false, "Whether connector plugins load from the plugin directory.", s => Bool(s.Connectors.EnablePlugins), (s, v) => s.Connectors.EnablePlugins = ParseBool(v), null, null, null));
            list.Add(Field("Connectors.PluginDirectory", "string", false, "Connector plugin directory.", s => s.Connectors.PluginDirectory, (s, v) => s.Connectors.PluginDirectory = v ?? "plugins", null, null, null));
            list.Add(Field("Connectors.DefaultToolTimeoutSeconds", "int", true, "Default tool timeout in seconds.", s => s.Connectors.DefaultToolTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.DefaultToolTimeoutSeconds = ParseInt(v), 1L, 3600L, null));
            list.Add(Field("Connectors.McpAllowStdio", "bool", false, "Whether mcp resources may launch local MCP servers over stdio (each resource must also set allowStdio).", s => Bool(s.Connectors.McpAllowStdio), (s, v) => s.Connectors.McpAllowStdio = ParseBool(v), null, null, null));
            list.Add(Field("Connectors.HealthIntervalSeconds", "int", false, "Seconds between resource health sweeps; 0 disables scheduled checks.", s => s.Connectors.HealthIntervalSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.HealthIntervalSeconds = ParseInt(v), 0L, 86400L, null));
            list.Add(Field("Connectors.HealthConnectorTypes", "string", false, "Comma-separated connector types allowed for scheduled health probes. Empty disables every type.", s => s.Connectors.HealthConnectorTypes, (s, v) => s.Connectors.HealthConnectorTypes = v ?? String.Empty, null, null, null));
            list.Add(Field("Connectors.TestTimeoutSeconds", "int", true, "Seconds after which a connection test is cancelled.", s => s.Connectors.TestTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.TestTimeoutSeconds = ParseInt(v), 1L, 600L, null));
            list.Add(Field("Connectors.ModelToolTimeoutSeconds", "int", true, "Timeout in seconds for tools that call the profile's model.", s => s.Connectors.ModelToolTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.ModelToolTimeoutSeconds = ParseInt(v), 1L, 3600L, null));
            list.Add(Field("Connectors.MaxConcurrencyPerResource", "int", true, "Concurrent tool calls allowed per resource.", s => s.Connectors.MaxConcurrencyPerResource.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.MaxConcurrencyPerResource = ParseInt(v), 1L, 256L, null));
            list.Add(Field("Connectors.SessionIdleMinutes", "int", true, "Minutes an idle pooled connector session is kept.", s => s.Connectors.SessionIdleMinutes.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.SessionIdleMinutes = ParseInt(v), 1L, 1440L, null));
            list.Add(Field("Connectors.ModelCallBudgetPerInvocation", "int", true, "Model calls one tool invocation may make.", s => s.Connectors.ModelCallBudgetPerInvocation.ToString(CultureInfo.InvariantCulture), (s, v) => s.Connectors.ModelCallBudgetPerInvocation = ParseInt(v), 0L, 10000L, null));
            list.Add(Field("Instructions.CompactBindingThreshold", "int", true, "Profiles with Auto instruction detail use compact connector instructions above this many bindings.", s => s.Instructions.CompactBindingThreshold.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.CompactBindingThreshold = ParseInt(v), 0L, 1000L, null));
            list.Add(Field("Instructions.MaxKnowledgeCharsPerResource", "int", true, "Characters of root resource knowledge included per resource in compiled instructions.", s => s.Instructions.MaxKnowledgeCharsPerResource.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.MaxKnowledgeCharsPerResource = ParseInt(v), 0L, 100000L, null));
            list.Add(Field("Instructions.CacheEntries", "int", true, "Compiled instruction cache entries.", s => s.Instructions.CacheEntries.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.CacheEntries = ParseInt(v), 0L, 100000L, null));
            list.Add(Field("Instructions.MaxThirdPartyChars", "int", false, "Characters of third-party guidance (external MCP server instructions, remote agent cards) included per binding in compiled instructions.", s => s.Instructions.MaxThirdPartyChars.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.MaxThirdPartyChars = ParseInt(v), 0L, 100000L, null));
            list.Add(Field("Instructions.ThirdPartyWaitMilliseconds", "int", false, "Milliseconds a compile waits for a first fetch of third-party guidance.", s => s.Instructions.ThirdPartyWaitMilliseconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.ThirdPartyWaitMilliseconds = ParseInt(v), 0L, 60000L, null));
            list.Add(Field("Instructions.ThirdPartyCacheSeconds", "int", false, "Seconds fetched third-party guidance is served before a background refresh.", s => s.Instructions.ThirdPartyCacheSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.ThirdPartyCacheSeconds = ParseInt(v), 0L, 86400L, null));
            list.Add(Field("Instructions.ThirdPartyFailureRetrySeconds", "int", false, "Seconds a failed third-party guidance fetch is remembered before a retry.", s => s.Instructions.ThirdPartyFailureRetrySeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.ThirdPartyFailureRetrySeconds = ParseInt(v), 0L, 86400L, null));
            list.Add(Field("Instructions.ThirdPartyCacheEntries", "int", false, "Cached third-party guidance entries.", s => s.Instructions.ThirdPartyCacheEntries.ToString(CultureInfo.InvariantCulture), (s, v) => s.Instructions.ThirdPartyCacheEntries = ParseInt(v), 1L, 1000000L, null));
            list.Add(Field("Models.HealthIntervalSeconds", "int", true, "Seconds between model endpoint health checks; 0 turns them off.", s => s.Models.HealthIntervalSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Models.HealthIntervalSeconds = ParseInt(v), 0L, 86400L, null));
            list.Add(Field("Mcp.Enable", "bool", false, "Whether the MCP surface is enabled.", s => Bool(s.Mcp.Enable), (s, v) => s.Mcp.Enable = ParseBool(v), null, null, null));
            list.Add(Field("Mcp.InternalPort", "int", false, "Loopback port of the internal MCP listener; 0 picks a free port.", s => s.Mcp.InternalPort.ToString(CultureInfo.InvariantCulture), (s, v) => s.Mcp.InternalPort = ParseInt(v), 0L, 65535L, null));
            list.Add(Field("Mcp.SessionIdleTimeoutSeconds", "int", true, "Seconds an idle MCP session lives.", s => s.Mcp.SessionIdleTimeoutSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Mcp.SessionIdleTimeoutSeconds = ParseInt(v), 60L, 604800L, null));
            list.Add(Field("Mcp.ProgressIntervalSeconds", "int", true, "Seconds between MCP progress notifications while a call waits for an approval.", s => s.Mcp.ProgressIntervalSeconds.ToString(CultureInfo.InvariantCulture), (s, v) => s.Mcp.ProgressIntervalSeconds = ParseInt(v), 1L, 60L, null));
            list.Add(Field("Mcp.MaxMessageSize", "int", false, "Largest MCP message in bytes.", s => s.Mcp.MaxMessageSize.ToString(CultureInfo.InvariantCulture), (s, v) => s.Mcp.MaxMessageSize = ParseInt(v), 1024L, 268435456L, null));
            list.Add(Field("A2A.Enable", "bool", false, "Whether the A2A surface is enabled.", s => Bool(s.A2A.Enable), (s, v) => s.A2A.Enable = ParseBool(v), null, null, null));
            list.Add(Field("OpenAI.Enable", "bool", false, "Whether the OpenAI-compatible surface is enabled.", s => Bool(s.OpenAI.Enable), (s, v) => s.OpenAI.Enable = ParseBool(v), null, null, null));
            return list;
        }

        #endregion
    }
}
