namespace Hugo.Connectors.Sql
{
    /// <summary>
    /// Setting keys and secret slot names shared by the SQL connectors. These are contract identifiers persisted on
    /// resources, not tunables. Thread-safe.
    /// </summary>
    public static class SqlSettingKeys
    {
        /// <summary>
        /// Host name or address.
        /// </summary>
        public const string Host = "host";

        /// <summary>
        /// TCP port.
        /// </summary>
        public const string Port = "port";

        /// <summary>
        /// Database name.
        /// </summary>
        public const string Database = "database";

        /// <summary>
        /// Default schema.
        /// </summary>
        public const string Schema = "schema";

        /// <summary>
        /// SQLite file path.
        /// </summary>
        public const string Filename = "filename";

        /// <summary>
        /// Login name.
        /// </summary>
        public const string Username = "username";

        /// <summary>
        /// TLS mode.
        /// </summary>
        public const string SslMode = "sslMode";

        /// <summary>
        /// SQL Server certificate trust.
        /// </summary>
        public const string TrustServerCertificate = "trustServerCertificate";

        /// <summary>
        /// Application name.
        /// </summary>
        public const string ApplicationName = "applicationName";

        /// <summary>
        /// Command timeout in seconds.
        /// </summary>
        public const string CommandTimeoutSeconds = "commandTimeoutSeconds";

        /// <summary>
        /// Connect timeout in seconds.
        /// </summary>
        public const string ConnectTimeoutSeconds = "connectTimeoutSeconds";

        /// <summary>
        /// Row cap for sql_query.
        /// </summary>
        public const string MaxRows = "maxRows";

        /// <summary>
        /// Read-only enforcement mode.
        /// </summary>
        public const string ReadOnlyEnforcement = "readOnlyEnforcement";

        /// <summary>
        /// Whether batches are allowed.
        /// </summary>
        public const string AllowMultipleStatements = "allowMultipleStatements";

        /// <summary>
        /// Schema cache TTL in minutes.
        /// </summary>
        public const string SchemaCacheMinutes = "schemaCacheMinutes";

        /// <summary>
        /// Secret slot holding a password (and optionally a username).
        /// </summary>
        public const string PasswordSlot = "password";

        /// <summary>
        /// Secret slot holding a full connection string.
        /// </summary>
        public const string ConnectionStringSlot = "connectionString";
    }
}
