namespace Hugo.Server.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Serialization;
    using Hugo.Core.Settings;

    /// <summary>
    /// Root of hugo.json (P1.1, Appendix C). Loaded from the settings file, then overridden by HUGO_* environment
    /// variables (Appendix D). Every section clamps its values to documented ranges; <see cref="Validate"/> rejects
    /// combinations that cannot work, naming the field path. Not thread-safe; the server treats it as read-mostly.
    /// </summary>
    public class HugoSettings
    {
        #region Public-Members

        /// <summary>
        /// When the file was first written (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Node identifier used in health and server info, or null for the host name.
        /// </summary>
        public string? NodeId { get; set; } = null;

        /// <summary>
        /// REST listener.
        /// </summary>
        public RestSettings Rest
        {
            get
            {
                return _Rest;
            }
            set
            {
                _Rest = value ?? new RestSettings();
            }
        }

        /// <summary>
        /// Application database.
        /// </summary>
        public DatabaseSettings Database
        {
            get
            {
                return _Database;
            }
            set
            {
                _Database = value ?? new DatabaseSettings();
            }
        }

        /// <summary>
        /// Logging.
        /// </summary>
        public LoggingSettings Logging
        {
            get
            {
                return _Logging;
            }
            set
            {
                _Logging = value ?? new LoggingSettings();
            }
        }

        /// <summary>
        /// Authentication.
        /// </summary>
        public AuthSettings Auth
        {
            get
            {
                return _Auth;
            }
            set
            {
                _Auth = value ?? new AuthSettings();
            }
        }

        /// <summary>
        /// Secrets vault.
        /// </summary>
        public SecretsSettings Secrets
        {
            get
            {
                return _Secrets;
            }
            set
            {
                _Secrets = value ?? new SecretsSettings();
            }
        }

        /// <summary>
        /// Request history capture.
        /// </summary>
        public RequestHistorySettings RequestHistory
        {
            get
            {
                return _RequestHistory;
            }
            set
            {
                _RequestHistory = value ?? new RequestHistorySettings();
            }
        }

        /// <summary>
        /// Telemetry.
        /// </summary>
        public TelemetrySettings Telemetry
        {
            get
            {
                return _Telemetry;
            }
            set
            {
                _Telemetry = value ?? new TelemetrySettings();
            }
        }

        /// <summary>
        /// Agent loop defaults.
        /// </summary>
        public AgentSettings Agent
        {
            get
            {
                return _Agent;
            }
            set
            {
                _Agent = value ?? new AgentSettings();
            }
        }

        /// <summary>
        /// Approvals.
        /// </summary>
        public ApprovalsSettings Approvals
        {
            get
            {
                return _Approvals;
            }
            set
            {
                _Approvals = value ?? new ApprovalsSettings();
            }
        }

        /// <summary>
        /// Connectors.
        /// </summary>
        public ConnectorsSettings Connectors
        {
            get
            {
                return _Connectors;
            }
            set
            {
                _Connectors = value ?? new ConnectorsSettings();
            }
        }

        /// <summary>
        /// Model endpoints.
        /// </summary>
        public ModelsSettings Models
        {
            get
            {
                return _Models;
            }
            set
            {
                _Models = value ?? new ModelsSettings();
            }
        }

        /// <summary>
        /// Instruction compilation.
        /// </summary>
        public InstructionsSettings Instructions
        {
            get
            {
                return _Instructions;
            }
            set
            {
                _Instructions = value ?? new InstructionsSettings();
            }
        }

        /// <summary>
        /// MCP surface.
        /// </summary>
        public McpSettings Mcp
        {
            get
            {
                return _Mcp;
            }
            set
            {
                _Mcp = value ?? new McpSettings();
            }
        }

        /// <summary>
        /// A2A surface.
        /// </summary>
        public A2ASettings A2A
        {
            get
            {
                return _A2A;
            }
            set
            {
                _A2A = value ?? new A2ASettings();
            }
        }

        /// <summary>
        /// OpenAI-compatible surface.
        /// </summary>
        public OpenAISettings OpenAI
        {
            get
            {
                return _OpenAI;
            }
            set
            {
                _OpenAI = value ?? new OpenAISettings();
            }
        }

        /// <summary>
        /// When true, the settings API never writes the settings file (for example a read-only mount). Default false.
        /// Environment: HUGO_SETTINGS_READONLY.
        /// </summary>
        public bool SettingsFileReadOnly { get; set; } = false;

        /// <summary>
        /// File the settings were loaded from.
        /// </summary>
        [JsonIgnore]
        public string? SourceFile { get; set; } = null;

        /// <summary>
        /// Field paths overridden by environment variables.
        /// </summary>
        [JsonIgnore]
        public HashSet<string> EnvironmentOverrides { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Node identifier: <see cref="NodeId"/>, or the host name.
        /// </summary>
        [JsonIgnore]
        public string EffectiveNodeId
        {
            get
            {
                if (!String.IsNullOrEmpty(NodeId)) return NodeId;
                string? host = Environment.GetEnvironmentVariable("HOSTNAME");
                return String.IsNullOrEmpty(host) ? Environment.MachineName : host;
            }
        }

        /// <summary>Managed prompt document storage and delivery limits.</summary>
        public PromptDocumentsSettings PromptDocuments { get { return _PromptDocuments; } set { _PromptDocuments = value ?? new PromptDocumentsSettings(); } }

        #endregion

        #region Private-Members

        private RestSettings _Rest = new RestSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private LoggingSettings _Logging = new LoggingSettings();
        private AuthSettings _Auth = new AuthSettings();
        private SecretsSettings _Secrets = new SecretsSettings();
        private RequestHistorySettings _RequestHistory = new RequestHistorySettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();
        private AgentSettings _Agent = new AgentSettings();
        private ApprovalsSettings _Approvals = new ApprovalsSettings();
        private ConnectorsSettings _Connectors = new ConnectorsSettings();
        private ModelsSettings _Models = new ModelsSettings();
        private McpSettings _Mcp = new McpSettings();
        private PromptDocumentsSettings _PromptDocuments = new PromptDocumentsSettings();
        private InstructionsSettings _Instructions = new InstructionsSettings();
        private A2ASettings _A2A = new A2ASettings();
        private OpenAISettings _OpenAI = new OpenAISettings();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load settings from a file, or defaults when the file does not exist.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        /// <exception cref="InvalidDataException">Thrown when the file is not valid JSON or has a value of the wrong type.</exception>
        public static HugoSettings FromFile(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            HugoSettings settings;
            if (File.Exists(path))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<HugoSettings>(File.ReadAllText(path), Serializer.FileOptions) ?? new HugoSettings();
                }
                catch (JsonException e)
                {
                    throw new InvalidDataException("Settings file '" + path + "' is invalid" + (String.IsNullOrEmpty(e.Path) ? String.Empty : " at " + e.Path) + ": " + e.Message, e);
                }
            }
            else
            {
                settings = new HugoSettings();
            }

            settings.SourceFile = path;
            return settings;
        }

        /// <summary>
        /// Write the settings to a file.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <exception cref="ArgumentNullException">Thrown when path is null or empty.</exception>
        public void ToFile(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Serializer.FileOptions));
        }

        /// <summary>
        /// Apply HUGO_* environment overrides (Appendix D). The secrets master keys (HUGO_MASTER_KEY,
        /// HUGO_MASTER_KEY_PREVIOUS) are read by the key loader directly and never copied into settings.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a variable holds a value of the wrong type, naming the variable and field path.</exception>
        public void ApplyEnvironment()
        {
            ApplyEnvironment(Environment.GetEnvironmentVariable);
        }

        /// <summary>
        /// Apply environment overrides from a lookup (tests supply their own).
        /// </summary>
        /// <param name="lookup">Variable lookup.</param>
        /// <exception cref="ArgumentNullException">Thrown when lookup is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a variable holds a value of the wrong type, naming the variable and field path.</exception>
        public void ApplyEnvironment(Func<string, string?> lookup)
        {
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));
            Str(lookup, "HUGO_NODE_ID", "NodeId", v => NodeId = v);
            DbType(lookup, "HUGO_DB_TYPE", "Database.Type", v => Database.Type = v);
            Str(lookup, "HUGO_DB_FILENAME", "Database.Filename", v => Database.Filename = v);
            Str(lookup, "HUGO_DB_SERVER", "Database.Host", v => Database.Host = v);
            Str(lookup, "HUGO_DB_HOST", "Database.Host", v => Database.Host = v);
            Int(lookup, "HUGO_DB_PORT", "Database.Port", v => Database.Port = v);
            Str(lookup, "HUGO_DB_DATABASE", "Database.DatabaseName", v => Database.DatabaseName = v);
            Str(lookup, "HUGO_DB_USERNAME", "Database.Username", v => Database.Username = v);
            Str(lookup, "HUGO_DB_PASSWORD", "Database.Password", v => Database.Password = v);
            Str(lookup, "HUGO_DB_SCHEMA", "Database.Schema", v => Database.Schema = v);
            Int(lookup, "HUGO_DB_MAX_POOL_SIZE", "Database.MaxPoolSize", v => Database.MaxPoolSize = v);
            Str(lookup, "HUGO_REST_HOSTNAME", "Rest.Hostname", v => Rest.Hostname = v);
            Int(lookup, "HUGO_REST_PORT", "Rest.Port", v => Rest.Port = v);
            Str(lookup, "HUGO_EXTERNAL_BASE_URL", "Rest.ExternalBaseUrl", v => Rest.ExternalBaseUrl = v);
            Str(lookup, "HUGO_AUTH_SIGNING_KEY", "Auth.SigningKey", v => Auth.SigningKey = v);
            Str(lookup, "HUGO_ADMIN_API_KEY", "Auth.AdminApiKey", v => Auth.AdminApiKey = v);
            Int(lookup, "HUGO_MASTER_KEY_VERSION", "Secrets.MasterKeyVersion", v => Secrets.MasterKeyVersion = v);
            Int(lookup, "HUGO_MASTER_KEY_PREVIOUS_VERSION", "Secrets.PreviousMasterKeyVersion", v => Secrets.PreviousMasterKeyVersion = v);
            Str(lookup, "HUGO_MASTER_KEY_FILE", "Secrets.MasterKeyFile", v => Secrets.MasterKeyFile = v);
            Str(lookup, "HUGO_OTLP_ENDPOINT", "Telemetry.OtlpEndpoint", v =>
            {
                Telemetry.OtlpEndpoint = v;
                Telemetry.OtlpEnable = true;
            });
            Bool(lookup, "HUGO_TELEMETRY_ENABLE", "Telemetry.Enabled", v => Telemetry.Enabled = v);
            Bool(lookup, "HUGO_ENABLE_PLUGINS", "Connectors.EnablePlugins", v => Connectors.EnablePlugins = v);
            Bool(lookup, "HUGO_SETTINGS_READONLY", "SettingsFileReadOnly", v => SettingsFileReadOnly = v);
        }

        /// <summary>
        /// Reject settings that cannot work.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the settings are invalid, naming the field path.</exception>
        public void Validate()
        {
            Database.Validate();
            if (PromptDocuments.PromptDocumentReadBytes > PromptDocuments.MaxPromptDocumentReadBytes) throw new ArgumentException("PromptDocuments.PromptDocumentReadBytes must not exceed MaxPromptDocumentReadBytes.");
            if (Database.Type == DatabaseTypeEnum.Postgresql && String.IsNullOrWhiteSpace(Database.Host)) throw new InvalidOperationException("Database.Host is required when Database.Type is Postgresql.");
            if (String.IsNullOrWhiteSpace(Auth.SigningKey) || Auth.SigningKey.Length < 16) throw new InvalidOperationException("Auth.SigningKey must be at least 16 characters.");
            if (!String.IsNullOrEmpty(Auth.AdminApiKey) && Auth.AdminApiKey.Length < 16) throw new InvalidOperationException("Auth.AdminApiKey must be empty (disabled) or at least 16 characters.");
            if (!String.IsNullOrEmpty(Rest.ExternalBaseUrl) && !Uri.TryCreate(Rest.ExternalBaseUrl, UriKind.Absolute, out Uri? _)) throw new InvalidOperationException("Rest.ExternalBaseUrl must be an absolute URL.");
            if (Mcp.Enable && Mcp.InternalPort != 0 && Mcp.InternalPort == Rest.Port) throw new InvalidOperationException("Mcp.InternalPort must differ from Rest.Port, or be 0 to pick a free port.");
        }

        #endregion

        #region Private-Methods

        private void Str(Func<string, string?> lookup, string variable, string path, Action<string> apply)
        {
            string? value = lookup(variable);
            if (String.IsNullOrEmpty(value)) return;
            apply(value);
            EnvironmentOverrides.Add(path);
        }

        private void Int(Func<string, string?> lookup, string variable, string path, Action<int> apply)
        {
            string? value = lookup(variable);
            if (String.IsNullOrEmpty(value)) return;
            if (!Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) throw new InvalidOperationException("Environment variable " + variable + " (" + path + ") must be an integer; got '" + value + "'.");
            apply(parsed);
            EnvironmentOverrides.Add(path);
        }

        private void Bool(Func<string, string?> lookup, string variable, string path, Action<bool> apply)
        {
            string? value = lookup(variable);
            if (String.IsNullOrEmpty(value)) return;
            if (!Boolean.TryParse(value, out bool parsed)) throw new InvalidOperationException("Environment variable " + variable + " (" + path + ") must be true or false; got '" + value + "'.");
            apply(parsed);
            EnvironmentOverrides.Add(path);
        }

        private void DbType(Func<string, string?> lookup, string variable, string path, Action<DatabaseTypeEnum> apply)
        {
            string? value = lookup(variable);
            if (String.IsNullOrEmpty(value)) return;
            if (!System.Enum.TryParse<DatabaseTypeEnum>(value, true, out DatabaseTypeEnum parsed)) throw new InvalidOperationException("Environment variable " + variable + " (" + path + ") must be Sqlite or Postgresql; got '" + value + "'.");
            apply(parsed);
            EnvironmentOverrides.Add(path);
        }

        #endregion
    }
}
