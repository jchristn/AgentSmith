namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Database.Sql;

    /// <summary>
    /// Builds the Hugo schema for a provider from a <see cref="SqlDialect"/> and a <see cref="TableCatalog"/>.
    /// Columns are snake_case. String columns use explicit VARCHAR lengths; provider-divergent types come from dialect
    /// tokens. Migrations are additive: append a new version, never edit a shipped one.
    /// </summary>
    public class SchemaBuilder
    {
        #region Public-Members

        /// <summary>
        /// Highest migration version this build knows. A database at a lower version is not ready for this build.
        /// </summary>
        public static int LatestVersion
        {
            get
            {
                SqlDialect dialect = new Hugo.Core.Database.Sqlite.Queries.SqliteDialect();
                int latest = 0;
                foreach (SchemaMigration migration in Build(new TableCatalog(null, null, dialect), dialect)) latest = Math.Max(latest, migration.Version);
                return latest;
            }
        }

        #endregion

        #region Private-Members

        private readonly TableCatalog _Catalog;
        private readonly SqlDialect _Dialect;
        private readonly List<string> _Statements = new List<string>();
        private readonly List<SchemaColumnAddition> _Columns = new List<SchemaColumnAddition>();

        #endregion

        #region Constructors-and-Factories

        private SchemaBuilder(TableCatalog catalog, SqlDialect dialect)
        {
            _Catalog = catalog;
            _Dialect = dialect;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the ordered, tracked migration list.
        /// </summary>
        /// <param name="catalog">Table catalog.</param>
        /// <param name="dialect">Provider dialect.</param>
        /// <returns>Migrations in ascending version order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static List<SchemaMigration> Build(TableCatalog catalog, SqlDialect dialect)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (dialect == null) throw new ArgumentNullException(nameof(dialect));

            List<SchemaMigration> migrations = new List<SchemaMigration>();

            SchemaBuilder v1 = new SchemaBuilder(catalog, dialect);
            v1.BuildVersion1();
            migrations.Add(new SchemaMigration(1, "Identity, security, request history, runtime settings, and prompt templates", v1._Statements, v1._Columns));

            SchemaBuilder v2 = new SchemaBuilder(catalog, dialect);
            v2.BuildVersion2();
            migrations.Add(new SchemaMigration(2, "Secrets vault: secrets, encrypted secret values, and master key versions", v2._Statements, v2._Columns));

            return migrations;
        }

        /// <summary>
        /// Build the DDL that creates the migration tracking table.
        /// </summary>
        /// <param name="catalog">Table catalog.</param>
        /// <param name="dialect">Provider dialect.</param>
        /// <returns>DDL statement.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static string MigrationsTableDdl(TableCatalog catalog, SqlDialect dialect)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (dialect == null) throw new ArgumentNullException(nameof(dialect));

            return dialect.CreateTable(catalog.Schema, catalog.RawName("schema_migrations"),
                "    version " + dialect.TypeInt + " NOT NULL PRIMARY KEY,\n" +
                "    description VARCHAR(1024) NOT NULL,\n" +
                "    applied_utc " + dialect.TypeTimestamp + " NOT NULL");
        }

        #endregion

        #region Private-Methods

        private void Table(string rawName, params string[] columns)
        {
            _Statements.Add(_Dialect.CreateTable(_Catalog.Schema, _Catalog.RawName(rawName), "    " + String.Join(",\n    ", columns)));
        }

        private void Index(string rawTable, string suffix, bool unique, params string[] columns)
        {
            List<string> quoted = new List<string>();
            foreach (string column in columns) quoted.Add(_Dialect.Quote(column));
            string name = "idx_" + _Catalog.RawName(rawTable) + "_" + suffix;
            _Statements.Add(_Dialect.CreateIndex(name, _Catalog.Schema, _Catalog.RawName(rawTable), String.Join(", ", quoted), unique));
        }

        private string Id()
        {
            return "id " + _Dialect.TypeId + " NOT NULL PRIMARY KEY";
        }

        private string Bool(string column)
        {
            return column + " " + _Dialect.TypeBool + " NOT NULL";
        }

        private string Timestamp(string column, bool required)
        {
            return column + " " + _Dialect.TypeTimestamp + (required ? " NOT NULL" : String.Empty);
        }

        private string Ref(string column, bool required)
        {
            return column + " " + _Dialect.TypeId + (required ? " NOT NULL" : String.Empty);
        }

        private string Text(string column, bool required)
        {
            return column + " " + _Dialect.TypeText + (required ? " NOT NULL" : String.Empty);
        }

        private void BuildVersion1()
        {
            SqlDialect d = _Dialect;

            // tenants
            Table("tenants",
                Id(),
                "name VARCHAR(256) NOT NULL",
                Text("description", false),
                Ref("parent_tenant_id", false),
                "region VARCHAR(128)",
                Ref("default_agent_profile_id", false),
                Bool("lockdown"),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("tenants", "name", true, "name");

            // users (system administrators are users with is_admin; see docs/PROVENANCE.md)
            Table("users",
                Id(),
                Ref("tenant_id", true),
                "first_name VARCHAR(128)",
                "last_name VARCHAR(128)",
                "email VARCHAR(256) NOT NULL",
                "password_hash VARCHAR(256) NOT NULL",
                Bool("is_admin"),
                Bool("is_tenant_admin"),
                Bool("must_change_password"),
                Ref("default_agent_profile_id", false),
                "preferred_locale VARCHAR(32)",
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("users", "tenant", false, "tenant_id");
            Index("users", "email", true, "tenant_id", "email");

            // credentials
            Table("credentials",
                Id(),
                Ref("tenant_id", true),
                Ref("owner_user_id", true),
                "name VARCHAR(256) NOT NULL",
                "access_key VARCHAR(128) NOT NULL",
                Text("secret_key_encrypted", true),
                "secret_key_last4 VARCHAR(8)",
                "auth_mode VARCHAR(64) NOT NULL",
                Ref("agent_profile_id", false),
                Bool("lock_to_profile"),
                Timestamp("last_used_utc", false),
                Timestamp("expires_utc", false),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("credentials", "tenant", false, "tenant_id");
            Index("credentials", "accesskey", true, "access_key");
            Index("credentials", "owner", false, "tenant_id", "owner_user_id");

            // auth_sessions
            Table("auth_sessions",
                Id(),
                Ref("tenant_id", false),
                Ref("user_id", false),
                Ref("credential_id", false),
                "principal_type VARCHAR(64) NOT NULL",
                "auth_scheme VARCHAR(64) NOT NULL",
                "token_id VARCHAR(64) NOT NULL",
                "source_ip VARCHAR(64)",
                "user_agent VARCHAR(512)",
                Timestamp("expires_utc", true),
                Timestamp("last_used_utc", false),
                Timestamp("revoked_utc", false),
                "revocation_reason VARCHAR(512)",
                Timestamp("reauthenticated_utc", false),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("auth_sessions", "tenant", false, "tenant_id");
            Index("auth_sessions", "tokenid", true, "token_id");
            Index("auth_sessions", "expires", false, "expires_utc");

            // roles (built-in roles have a null tenant_id)
            Table("roles",
                Id(),
                Ref("tenant_id", false),
                "name VARCHAR(128) NOT NULL",
                Text("description", false),
                Bool("is_builtin"),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("roles", "tenant", false, "tenant_id");
            Index("roles", "name", false, "tenant_id", "name");

            // permissions, with normalized resource type and operation type child tables
            Table("permissions",
                Id(),
                Ref("tenant_id", false),
                "name VARCHAR(256) NOT NULL",
                "permission_type VARCHAR(64) NOT NULL",
                Bool("is_builtin"),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("permissions", "tenant", false, "tenant_id");
            Index("permissions", "name", false, "tenant_id", "name");
            Table("permission_resource_types",
                Id(),
                Ref("permission_id", true),
                Ref("tenant_id", false),
                "resource_type VARCHAR(256) NOT NULL");
            Index("permission_resource_types", "parent", false, "permission_id");
            Table("permission_operation_types",
                Id(),
                Ref("permission_id", true),
                Ref("tenant_id", false),
                "operation_type VARCHAR(256) NOT NULL");
            Index("permission_operation_types", "parent", false, "permission_id");

            // role_permission_maps
            Table("role_permission_maps",
                Id(),
                Ref("tenant_id", false),
                Ref("role_id", true),
                Ref("permission_id", true),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("role_permission_maps", "tenant", false, "tenant_id");
            Index("role_permission_maps", "role", false, "role_id");
            Index("role_permission_maps", "rolepermission", true, "role_id", "permission_id");

            // user_role_assignments
            Table("user_role_assignments",
                Id(),
                Ref("tenant_id", true),
                Ref("user_id", true),
                Ref("role_id", false),
                "role_name VARCHAR(128)",
                "resource_scope VARCHAR(64) NOT NULL",
                Ref("resource_id", false),
                Bool("inherits_to_children"),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("user_role_assignments", "tenant", false, "tenant_id");
            Index("user_role_assignments", "user", false, "tenant_id", "user_id");

            // credential_scope_assignments
            Table("credential_scope_assignments",
                Id(),
                Ref("tenant_id", true),
                Ref("credential_id", true),
                Ref("role_id", false),
                "role_name VARCHAR(128)",
                "resource_scope VARCHAR(64) NOT NULL",
                Ref("resource_id", false),
                Bool("inherits_to_children"),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("credential_scope_assignments", "tenant", false, "tenant_id");
            Index("credential_scope_assignments", "credential", false, "tenant_id", "credential_id");

            // audit_records
            Table("audit_records",
                Id(),
                Ref("tenant_id", false),
                "event_type VARCHAR(64) NOT NULL",
                "request_id VARCHAR(64)",
                "correlation_id VARCHAR(64)",
                "trace_id VARCHAR(64)",
                "auth_scheme VARCHAR(64)",
                "principal_type VARCHAR(64)",
                Ref("principal_id", false),
                "principal_name VARCHAR(256)",
                Ref("session_id", false),
                "resource_type VARCHAR(64)",
                Ref("resource_id", false),
                "operation VARCHAR(64)",
                "request_type VARCHAR(128)",
                "method VARCHAR(16)",
                "path VARCHAR(1024)",
                "source_ip VARCHAR(64)",
                "auth_result VARCHAR(64)",
                "authorization_result VARCHAR(64)",
                Text("reason", false),
                "bypass_reason VARCHAR(256)",
                "status_code " + d.TypeInt + " NOT NULL",
                Text("description", false),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("audit_records", "tenant", false, "tenant_id");
            Index("audit_records", "created", false, "tenant_id", "created_utc");
            Index("audit_records", "eventtype", false, "event_type");

            // request_history, with headers in a child table
            Table("request_history",
                Id(),
                Ref("tenant_id", false),
                Ref("user_id", false),
                "principal_name VARCHAR(256)",
                "method VARCHAR(16) NOT NULL",
                "path VARCHAR(1024) NOT NULL",
                Text("url", true),
                "status_code " + d.TypeInt + " NOT NULL",
                "duration_ms " + d.TypeDouble + " NOT NULL",
                "source_ip VARCHAR(64)",
                "trace_id VARCHAR(64)",
                "correlation_id VARCHAR(64)",
                Text("request_body", false),
                "request_body_bytes " + d.TypeBigInt + " NOT NULL",
                Bool("request_body_truncated"),
                Text("response_body", false),
                "response_body_bytes " + d.TypeBigInt + " NOT NULL",
                Bool("response_body_truncated"),
                Timestamp("created_utc", true),
                Timestamp("completed_utc", false));
            Index("request_history", "tenant", false, "tenant_id");
            Index("request_history", "created", false, "created_utc");
            Index("request_history", "tenantcreated", false, "tenant_id", "created_utc");
            Table("request_history_headers",
                Id(),
                Ref("request_history_entry_id", true),
                Ref("tenant_id", false),
                "direction VARCHAR(64) NOT NULL",
                "name VARCHAR(256) NOT NULL",
                Text("value", true));
            Index("request_history_headers", "parent", false, "request_history_entry_id");

            // prompt_templates (built-in templates have a null tenant_id), variables, and versions
            Table("prompt_templates",
                Id(),
                Ref("tenant_id", false),
                "name VARCHAR(256) NOT NULL",
                Text("description", false),
                "kind VARCHAR(64) NOT NULL",
                Text("content", true),
                "current_version " + d.TypeInt + " NOT NULL",
                Bool("is_builtin"),
                "builtin_key VARCHAR(128)",
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("prompt_templates", "tenant", false, "tenant_id");
            Index("prompt_templates", "name", false, "tenant_id", "name");
            Table("prompt_template_variables",
                Id(),
                Ref("prompt_template_id", true),
                Ref("tenant_id", false),
                "name VARCHAR(256) NOT NULL",
                Text("description", false),
                Bool("required"),
                Text("default_value", false));
            Index("prompt_template_variables", "parent", false, "prompt_template_id");
            Table("prompt_template_versions",
                Id(),
                Ref("tenant_id", false),
                Ref("template_id", true),
                "version " + d.TypeInt + " NOT NULL",
                Text("content", true),
                Text("change_note", false),
                "created_by VARCHAR(256)",
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("prompt_template_versions", "tenant", false, "tenant_id");
            Index("prompt_template_versions", "templateversion", true, "template_id", "version");

            // cluster_settings: runtime settings editable through the settings API (global)
            Table("cluster_settings",
                Id(),
                "setting_key VARCHAR(256) NOT NULL",
                Text("setting_value", false),
                "updated_by VARCHAR(256)",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("cluster_settings", "key", true, "setting_key");

            // request_nonces: signed-request replay protection (global, keyed by access key and nonce)
            Table("request_nonces",
                "nonce_key VARCHAR(512) NOT NULL PRIMARY KEY",
                Timestamp("expires_utc", true),
                Timestamp("created_utc", true));
            Index("request_nonces", "expires", false, "expires_utc");

            // rate_limit_buckets: fixed-window counters for authentication and reveal rate limits (global)
            Table("rate_limit_buckets",
                "bucket_key VARCHAR(512) NOT NULL PRIMARY KEY",
                "hits " + d.TypeInt + " NOT NULL",
                Timestamp("expires_utc", true));
            Index("rate_limit_buckets", "expires", false, "expires_utc");
        }

        private void BuildVersion2()
        {
            // secrets: metadata only; encrypted values live in secret_values (plan section 12)
            Table("secrets",
                Id(),
                Ref("tenant_id", true),
                Ref("owner_user_id", false),
                "name VARCHAR(256) NOT NULL",
                "secret_kind VARCHAR(64) NOT NULL",
                Text("description", false),
                "key_version " + _Dialect.TypeInt + " NOT NULL",
                "fingerprint VARCHAR(128)",
                Timestamp("last_rotated_utc", false),
                Timestamp("last_revealed_utc", false),
                Timestamp("expires_utc", false),
                Bool("active"),
                Bool("is_protected"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("secrets", "tenant", false, "tenant_id");
            Index("secrets", "name", true, "tenant_id", "name");
            Index("secrets", "keyversion", false, "key_version");

            // secret_values: one encrypted field per row, bound to tenantId|secretId|fieldName as associated data
            Table("secret_values",
                Id(),
                Ref("secret_id", true),
                Ref("tenant_id", true),
                "field_name VARCHAR(128) NOT NULL",
                Text("ciphertext", true),
                "key_version " + _Dialect.TypeInt + " NOT NULL",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("secret_values", "parent", true, "secret_id", "field_name");
            Index("secret_values", "keyversion", false, "key_version");

            // master_key_versions: one canary row per master key version; startup refuses to run when no configured key
            // decrypts the canary of the current version (global)
            Table("master_key_versions",
                Id(),
                "key_version " + _Dialect.TypeInt + " NOT NULL",
                Text("canary", true),
                Bool("is_current"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("master_key_versions", "version", true, "key_version");
        }

        #endregion
    }
}
