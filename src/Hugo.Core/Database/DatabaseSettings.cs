namespace Hugo.Core.Database
{
    using System;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;

    /// <summary>
    /// Database connection settings.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Provider. Default Sqlite for development; use Postgresql for clusters.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// Sqlite database file path. Default data/hugo.db.
        /// </summary>
        public string Filename
        {
            get
            {
                return _Filename;
            }
            set
            {
                _Filename = String.IsNullOrWhiteSpace(value) ? "data/hugo.db" : value;
            }
        }

        /// <summary>
        /// Postgresql host. Default 127.0.0.1.
        /// </summary>
        public string Host
        {
            get
            {
                return _Host;
            }
            set
            {
                _Host = String.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value;
            }
        }

        /// <summary>
        /// Postgresql port. Minimum 1, maximum 65535, default 5432.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Database name. Default hugo.
        /// </summary>
        public string DatabaseName
        {
            get
            {
                return _DatabaseName;
            }
            set
            {
                _DatabaseName = String.IsNullOrWhiteSpace(value) ? "hugo" : value;
            }
        }

        /// <summary>
        /// Username. Default postgres.
        /// </summary>
        public string Username
        {
            get
            {
                return _Username;
            }
            set
            {
                _Username = String.IsNullOrWhiteSpace(value) ? "postgres" : value;
            }
        }

        /// <summary>
        /// Password. Override with HUGO_DB_PASSWORD.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Optional schema (Postgresql only).
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// Optional table name prefix.
        /// </summary>
        public string? TablePrefix { get; set; } = null;

        /// <summary>
        /// Whether Hugo creates and migrates the schema. When false, tables are verified instead. Default true.
        /// </summary>
        public bool ManageSchema { get; set; } = true;

        /// <summary>
        /// Maximum connection pool size. Minimum 1, maximum 1000, default 100.
        /// </summary>
        public int MaxPoolSize
        {
            get
            {
                return _MaxPoolSize;
            }
            set
            {
                _MaxPoolSize = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Minimum connections the Postgresql pool keeps open per replica. Minimum 0, maximum 1000, default 0. Values
        /// above <see cref="MaxPoolSize"/> are lowered to it when the connection string is built.
        /// </summary>
        public int MinPoolSize
        {
            get
            {
                return _MinPoolSize;
            }
            set
            {
                _MinPoolSize = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Seconds to wait for a free pooled connection (or a new one) before failing. Minimum 1, maximum 600, default 15.
        /// Postgresql only.
        /// </summary>
        public int ConnectionTimeoutSeconds
        {
            get
            {
                return _ConnectionTimeoutSeconds;
            }
            set
            {
                _ConnectionTimeoutSeconds = Math.Clamp(value, 1, 600);
            }
        }

        /// <summary>
        /// Seconds a single command may run before it is cancelled. Minimum 0 (no limit), maximum 3600, default 30.
        /// Postgresql only.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 0, 3600);
            }
        }

        /// <summary>
        /// Seconds an idle pooled connection is kept before it is closed. Minimum 1, maximum 3600, default 300.
        /// Postgresql only.
        /// </summary>
        public int ConnectionIdleLifetimeSeconds
        {
            get
            {
                return _ConnectionIdleLifetimeSeconds;
            }
            set
            {
                _ConnectionIdleLifetimeSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Additional provider connection string options, appended verbatim.
        /// </summary>
        public string? AdditionalOptions { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Filename = "data/hugo.db";
        private string _Host = "127.0.0.1";
        private int _Port = 5432;
        private string _DatabaseName = "hugo";
        private string _Username = "postgres";
        private int _MaxPoolSize = 100;
        private int _MinPoolSize = 0;
        private int _ConnectionTimeoutSeconds = 15;
        private int _CommandTimeoutSeconds = 30;
        private int _ConnectionIdleLifetimeSeconds = 300;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate that the settings are coherent for the selected provider.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when a required field is missing.</exception>
        public void Validate()
        {
            if (Type == DatabaseTypeEnum.Sqlite)
            {
                if (String.IsNullOrEmpty(_Filename)) throw new ArgumentException("Sqlite requires a database Filename.", nameof(Filename));
                return;
            }

            if (String.IsNullOrEmpty(_Host)) throw new ArgumentException("A database Host is required for provider '" + Type + "'.", nameof(Host));
            if (String.IsNullOrEmpty(_DatabaseName)) throw new ArgumentException("A DatabaseName is required for provider '" + Type + "'.", nameof(DatabaseName));
        }

        /// <summary>
        /// Build an Npgsql connection string.
        /// </summary>
        /// <returns>Connection string.</returns>
        public string ToPostgresqlConnectionString()
        {
            return
                "Host=" + _Host + ";" +
                "Port=" + _Port + ";" +
                "Database=" + _DatabaseName + ";" +
                "Username=" + _Username + ";" +
                "Password=" + Password + ";" +
                "Maximum Pool Size=" + _MaxPoolSize + ";" +
                "Minimum Pool Size=" + Math.Min(_MinPoolSize, _MaxPoolSize) + ";" +
                "Timeout=" + _ConnectionTimeoutSeconds + ";" +
                "Command Timeout=" + _CommandTimeoutSeconds + ";" +
                "Connection Idle Lifetime=" + _ConnectionIdleLifetimeSeconds + ";" +
                AppendOptions();
        }

        /// <summary>
        /// Build an Npgsql connection string for one long-lived, unpooled connection (the LISTEN connection of the
        /// Postgresql notification backplane). It does not take a slot from the pool, and TCP keepalives detect a dead
        /// peer.
        /// </summary>
        /// <returns>Connection string.</returns>
        public string ToPostgresqlListenerConnectionString()
        {
            return
                "Host=" + _Host + ";" +
                "Port=" + _Port + ";" +
                "Database=" + _DatabaseName + ";" +
                "Username=" + _Username + ";" +
                "Password=" + Password + ";" +
                "Pooling=false;" +
                "Timeout=" + _ConnectionTimeoutSeconds + ";" +
                "Keepalive=30;" +
                AppendOptions();
        }

        /// <summary>
        /// Build a Microsoft.Data.Sqlite connection string.
        /// </summary>
        /// <returns>Connection string.</returns>
        public string ToSqliteConnectionString()
        {
            return "Data Source=" + _Filename + ";Foreign Keys=False;Pooling=True;" + AppendOptions();
        }

        /// <summary>
        /// Clone the settings.
        /// </summary>
        /// <returns>Copy.</returns>
        public DatabaseSettings Clone()
        {
            return (DatabaseSettings)MemberwiseClone();
        }

        #endregion

        #region Private-Methods

        private string AppendOptions()
        {
            if (String.IsNullOrEmpty(AdditionalOptions)) return String.Empty;
            string trimmed = AdditionalOptions.Trim();
            return trimmed.EndsWith(";", StringComparison.Ordinal) ? trimmed : trimmed + ";";
        }

        #endregion
    }
}
