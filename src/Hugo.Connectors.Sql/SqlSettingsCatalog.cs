namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds the settings schema and secret slots of each SQL connector (plan section 9.1). Thread-safe.
    /// </summary>
    public static class SqlSettingsCatalog
    {
        #region Public-Methods

        /// <summary>
        /// Build the settings schema for an engine.
        /// </summary>
        /// <param name="engine">Engine.</param>
        /// <returns>Schema.</returns>
        public static ConnectorSettingsSchema BuildSettings(SqlEngineEnum engine)
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();

            if (engine == SqlEngineEnum.Sqlite)
            {
                schema.Add(new SettingDescriptor(SqlSettingKeys.Filename, SettingValueKindEnum.String, "Database file")
                {
                    Help = "Path to the SQLite database file as seen by the Hugo server. Required unless a connectionString secret is bound. The file is never created.",
                    Group = "Connection",
                    ShowInSummary = true
                });
            }
            else
            {
                schema.Add(new SettingDescriptor(SqlSettingKeys.Host, SettingValueKindEnum.String, "Host")
                {
                    Help = "Server host name or IP address. Required unless a connectionString secret is bound.",
                    Group = "Connection",
                    ShowInSummary = true
                });
                schema.Add(new SettingDescriptor(SqlSettingKeys.Port, SettingValueKindEnum.Integer, "Port")
                {
                    Help = "TCP port. Defaults to the engine's standard port.",
                    DefaultValue = DefaultPort(engine).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Minimum = 1,
                    Maximum = 65535,
                    Group = "Connection"
                });
                schema.Add(new SettingDescriptor(SqlSettingKeys.Database, SettingValueKindEnum.String, "Database")
                {
                    Help = "Database to connect to.",
                    Group = "Connection",
                    ShowInSummary = true
                });
                schema.Add(new SettingDescriptor(SqlSettingKeys.Username, SettingValueKindEnum.String, "Username")
                {
                    Help = "Login name. A username field on the bound password secret overrides this value. Prefer a least-privilege, read-only login.",
                    Group = "Connection"
                });
            }

            schema.Add(new SettingDescriptor(SqlSettingKeys.Schema, SettingValueKindEnum.String, "Default schema")
            {
                Help = DefaultSchemaHelp(engine),
                Group = "Connection"
            });

            if (engine != SqlEngineEnum.Sqlite)
            {
                schema.Add(new SettingDescriptor(SqlSettingKeys.SslMode, SettingValueKindEnum.Enum, "TLS mode")
                {
                    Help = SslModeHelp(engine),
                    DefaultValue = "Prefer",
                    AllowedValues = new List<string> { "Disable", "Prefer", "Require", "VerifyCA", "VerifyFull" },
                    Group = "Security"
                });
            }

            if (engine == SqlEngineEnum.SqlServer)
            {
                schema.Add(new SettingDescriptor(SqlSettingKeys.TrustServerCertificate, SettingValueKindEnum.Boolean, "Trust server certificate")
                {
                    Help = "Accept the server's TLS certificate without validation. Only for development servers with self-signed certificates.",
                    DefaultValue = "false",
                    Group = "Security"
                });
            }

            if (engine != SqlEngineEnum.Sqlite)
            {
                schema.Add(new SettingDescriptor(SqlSettingKeys.ApplicationName, SettingValueKindEnum.String, "Application name")
                {
                    Help = "Name reported to the server so DBAs can identify Hugo sessions.",
                    DefaultValue = "hugo",
                    Group = "Connection"
                });
                schema.Add(new SettingDescriptor(SqlSettingKeys.ConnectTimeoutSeconds, SettingValueKindEnum.Integer, "Connect timeout (seconds)")
                {
                    Help = "Seconds to wait for a connection. Minimum 1, maximum 300.",
                    DefaultValue = "15",
                    Minimum = 1,
                    Maximum = 300,
                    Group = "Limits"
                });
            }

            schema.Add(new SettingDescriptor(SqlSettingKeys.CommandTimeoutSeconds, SettingValueKindEnum.Integer, "Command timeout (seconds)")
            {
                Help = "Seconds a statement may run before it is cancelled on the server. The binding or profile timeout applies when smaller. Minimum 1, maximum 3600.",
                DefaultValue = "30",
                Minimum = 1,
                Maximum = 3600,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor(SqlSettingKeys.MaxRows, SettingValueKindEnum.Integer, "Maximum rows")
            {
                Help = "Rows returned by sql_query before the result is truncated. The invocation row limit applies when smaller. Minimum 1, maximum 100000.",
                DefaultValue = "500",
                Minimum = 1,
                Maximum = 100000,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor(SqlSettingKeys.SchemaCacheMinutes, SettingValueKindEnum.Integer, "Schema cache (minutes)")
            {
                Help = "Minutes a sql_describe_schema crawl is reused. 0 disables caching. Maximum 1440.",
                DefaultValue = "10",
                Minimum = 0,
                Maximum = 1440,
                Group = "Limits"
            });
            schema.Add(new SettingDescriptor(SqlSettingKeys.ReadOnlyEnforcement, SettingValueKindEnum.Enum, "Read-only enforcement")
            {
                Help = ReadOnlyHelp(engine),
                DefaultValue = "Session",
                AllowedValues = new List<string> { "Session", "ClassifierOnly" },
                Group = "Safety"
            });
            schema.Add(new SettingDescriptor(SqlSettingKeys.AllowMultipleStatements, SettingValueKindEnum.Boolean, "Allow multiple statements")
            {
                Help = "Allow sql_query to run several statements in one call. The batch is classified by its most severe statement. Default false.",
                DefaultValue = "false",
                Group = "Safety"
            });
            return schema;
        }

        /// <summary>
        /// Build the secret slots for an engine.
        /// </summary>
        /// <param name="engine">Engine.</param>
        /// <returns>Slots.</returns>
        public static List<SecretSlotDescriptor> BuildSecretSlots(SqlEngineEnum engine)
        {
            List<SecretSlotDescriptor> slots = new List<SecretSlotDescriptor>();
            if (engine != SqlEngineEnum.Sqlite)
            {
                slots.Add(new SecretSlotDescriptor(SqlSettingKeys.PasswordSlot, "Password", SecretKindEnum.Password)
                {
                    Help = "Password for the login. When the secret also carries a username, it overrides the username setting.",
                    Required = false
                });
            }
            slots.Add(new SecretSlotDescriptor(SqlSettingKeys.ConnectionStringSlot, "Connection string", SecretKindEnum.ConnectionString)
            {
                Help = ConnectionStringHelp(engine),
                Required = false
            });
            return slots;
        }

        /// <summary>
        /// Help text of the <c>sslMode</c> setting for an engine.
        /// </summary>
        /// <param name="engine">Engine.</param>
        /// <returns>Help text.</returns>
        public static string SslModeHelp(SqlEngineEnum engine)
        {
            if (engine == SqlEngineEnum.SqlServer)
                return "Disable: Encrypt=false, no TLS beyond the login exchange SQL Server always encrypts (the server can still force encryption). "
                    + "Prefer (default): Encrypt=Optional, which SqlClient treats the same as Encrypt=false; it has no opportunistic mode. "
                    + "Require: Encrypt=true, validating the certificate unless trustServerCertificate is on. VerifyCA and VerifyFull: Encrypt=true with certificate validation.";
            return "Disable: no TLS. Prefer: TLS when the server offers it (default). Require: TLS without certificate validation. VerifyCA and VerifyFull: TLS with certificate validation.";
        }

        /// <summary>
        /// Help text of the <c>connectionString</c> secret slot for an engine.
        /// </summary>
        /// <param name="engine">Engine.</param>
        /// <returns>Help text.</returns>
        public static string ConnectionStringHelp(SqlEngineEnum engine)
        {
            string common = "A complete driver connection string. When bound, it overrides the discrete connection settings; Hugo still applies the command timeout and the application name when absent";
            if (engine == SqlEngineEnum.SqlServer)
                return common + ". SQL Server has no session-level read-only mode, so read-only access rests on the statement classifier and the login's permissions; bind a login that can only read.";
            if (engine == SqlEngineEnum.Sqlite)
                return common + ", and, with readOnlyEnforcement Session, opens the file read-only (Mode=ReadOnly) for Read calls.";
            return common + ", and, with readOnlyEnforcement Session, runs Read calls in a read-only transaction.";
        }

        /// <summary>
        /// Standard TCP port of an engine.
        /// </summary>
        /// <param name="engine">Engine.</param>
        /// <returns>Port, or 0 for SQLite.</returns>
        public static int DefaultPort(SqlEngineEnum engine)
        {
            switch (engine)
            {
                case SqlEngineEnum.Mysql:
                    return 3306;
                case SqlEngineEnum.SqlServer:
                    return 1433;
                case SqlEngineEnum.Postgresql:
                    return 5432;
                default:
                    return 0;
            }
        }

        #endregion

        #region Private-Methods

        private static string DefaultSchemaHelp(SqlEngineEnum engine)
        {
            switch (engine)
            {
                case SqlEngineEnum.Mysql:
                    return "Schema (database) used by catalog tools when none is given. Defaults to the connection database.";
                case SqlEngineEnum.SqlServer:
                    return "Schema used by catalog tools when none is given. Default dbo.";
                case SqlEngineEnum.Postgresql:
                    return "Schema used by catalog tools when none is given. Default public.";
                default:
                    return "Schema used by catalog tools when none is given: main, or the name of an attached database. Default main.";
            }
        }

        private static string ReadOnlyHelp(SqlEngineEnum engine)
        {
            switch (engine)
            {
                case SqlEngineEnum.Mysql:
                    return "Session: reads run inside START TRANSACTION READ ONLY, so MySQL itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone.";
                case SqlEngineEnum.SqlServer:
                    return "SQL Server has no session read-only mode, so both values rely on Hugo's statement classifier. Bind a login that is only a member of db_datareader for read-only use.";
                case SqlEngineEnum.Postgresql:
                    return "Session: reads run inside a transaction with SET TRANSACTION READ ONLY, so PostgreSQL itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone.";
                default:
                    return "Session: reads open the file with Mode=ReadOnly, so SQLite itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone.";
            }
        }

        #endregion
    }
}
