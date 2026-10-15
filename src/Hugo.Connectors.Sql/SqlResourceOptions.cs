namespace Hugo.Connectors.Sql
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed view of a SQL resource's settings and secrets for one call. Holds plaintext secret material only for the
    /// life of the call; never log or return it. Not thread-safe for mutation.
    /// </summary>
    public class SqlResourceOptions
    {
        #region Public-Members

        /// <summary>
        /// Host name or address. Ignored for SQLite.
        /// </summary>
        public string? Host { get; set; } = null;

        /// <summary>
        /// TCP port, or null for the engine default (MySQL 3306, SQL Server 1433, PostgreSQL 5432).
        /// </summary>
        public int? Port { get; set; } = null;

        /// <summary>
        /// Database name.
        /// </summary>
        public string? Database { get; set; } = null;

        /// <summary>
        /// Default schema for catalog tools, or null for the engine default (<c>public</c>, <c>dbo</c>, the database
        /// for MySQL, <c>main</c> for SQLite).
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// SQLite database file path.
        /// </summary>
        public string? Filename { get; set; } = null;

        /// <summary>
        /// Login name. A <c>username</c> field on the password secret overrides the setting.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Password from the <c>password</c> secret slot.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Full connection string from the <c>connectionString</c> secret slot. Overrides the discrete fields.
        /// </summary>
        public string? ConnectionString { get; set; } = null;

        /// <summary>
        /// TLS mode: <c>Disable</c>, <c>Prefer</c> (default), <c>Require</c>, <c>VerifyCA</c>, or <c>VerifyFull</c>.
        /// </summary>
        public string SslMode { get; set; } = "Prefer";

        /// <summary>
        /// SQL Server only: trust the server certificate without validation. Default false.
        /// </summary>
        public bool TrustServerCertificate { get; set; } = false;

        /// <summary>
        /// Application name reported to the server. Default <c>hugo</c>.
        /// </summary>
        public string ApplicationName { get; set; } = "hugo";

        /// <summary>
        /// Per-command timeout in seconds. Minimum 1, maximum 3600, default 30. The effective timeout is the smaller of
        /// this and the invocation timeout.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Connection timeout in seconds. Minimum 1, maximum 300, default 15.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                _ConnectTimeoutSeconds = Math.Clamp(value, 1, 300);
            }
        }

        /// <summary>
        /// Maximum rows returned by <c>sql_query</c>. Minimum 1, maximum 100000, default 500. The effective cap is
        /// the smaller of this and the invocation's row limit.
        /// </summary>
        public int MaxRows
        {
            get
            {
                return _MaxRows;
            }
            set
            {
                _MaxRows = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Read-only enforcement mode. Default <see cref="ReadOnlyEnforcementEnum.Session"/>.
        /// </summary>
        public ReadOnlyEnforcementEnum ReadOnlyEnforcement { get; set; } = ReadOnlyEnforcementEnum.Session;

        /// <summary>
        /// Whether <c>sql_query</c> accepts more than one statement per call. Default false.
        /// </summary>
        public bool AllowMultipleStatements { get; set; } = false;

        /// <summary>
        /// Minutes a <c>sql_describe_schema</c> crawl stays cached. Minimum 0 (no caching), maximum 1440, default 10.
        /// </summary>
        public int SchemaCacheMinutes
        {
            get
            {
                return _SchemaCacheMinutes;
            }
            set
            {
                _SchemaCacheMinutes = Math.Clamp(value, 0, 1440);
            }
        }

        #endregion

        #region Private-Members

        private int _CommandTimeoutSeconds = 30;
        private int _ConnectTimeoutSeconds = 15;
        private int _MaxRows = 500;
        private int _SchemaCacheMinutes = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public SqlResourceOptions()
        {
        }

        /// <summary>
        /// Read options from a resource context.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static SqlResourceOptions FromResource(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ResourceSettings s = resource.Settings;
            SqlResourceOptions o = new SqlResourceOptions();
            o.Host = s.GetString(SqlSettingKeys.Host);
            long port = s.GetLong(SqlSettingKeys.Port, 0);
            o.Port = port > 0 && port <= 65535 ? (int)port : null;
            o.Database = s.GetString(SqlSettingKeys.Database);
            o.Schema = s.GetString(SqlSettingKeys.Schema);
            o.Filename = s.GetString(SqlSettingKeys.Filename);
            o.Username = s.GetString(SqlSettingKeys.Username);
            o.SslMode = s.GetString(SqlSettingKeys.SslMode, "Prefer") ?? "Prefer";
            o.TrustServerCertificate = s.GetBool(SqlSettingKeys.TrustServerCertificate, false);
            o.ApplicationName = s.GetString(SqlSettingKeys.ApplicationName, "hugo") ?? "hugo";
            o.CommandTimeoutSeconds = s.GetInt(SqlSettingKeys.CommandTimeoutSeconds, 30, 1, 3600);
            o.ConnectTimeoutSeconds = s.GetInt(SqlSettingKeys.ConnectTimeoutSeconds, 15, 1, 300);
            o.MaxRows = s.GetInt(SqlSettingKeys.MaxRows, 500, 1, 100000);
            o.AllowMultipleStatements = s.GetBool(SqlSettingKeys.AllowMultipleStatements, false);
            o.SchemaCacheMinutes = s.GetInt(SqlSettingKeys.SchemaCacheMinutes, 10, 0, 1440);
            string enforcement = s.GetString(SqlSettingKeys.ReadOnlyEnforcement, "Session") ?? "Session";
            o.ReadOnlyEnforcement = String.Equals(enforcement, "ClassifierOnly", StringComparison.OrdinalIgnoreCase)
                ? ReadOnlyEnforcementEnum.ClassifierOnly
                : ReadOnlyEnforcementEnum.Session;

            SecretAccessor secrets = resource.Secrets;
            o.ConnectionString = secrets.Get(SqlSettingKeys.ConnectionStringSlot, "connectionString");
            o.Password = secrets.Get(SqlSettingKeys.PasswordSlot, "password");
            string? secretUser = secrets.Get(SqlSettingKeys.PasswordSlot, "username");
            if (!String.IsNullOrEmpty(secretUser)) o.Username = secretUser;
            return o;
        }

        #endregion
    }
}
