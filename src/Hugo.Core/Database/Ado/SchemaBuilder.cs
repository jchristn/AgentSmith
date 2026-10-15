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

        /// <summary>
        /// Number of migrations this build defines. Versions need not be contiguous: migrations are tracked as a set, so
        /// parallel branches can claim distinct version numbers.
        /// </summary>
        public static int MigrationCount
        {
            get
            {
                SqlDialect dialect = new Hugo.Core.Database.Sqlite.Queries.SqliteDialect();
                return Build(new TableCatalog(null, null, dialect), dialect).Count;
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

            SchemaBuilder v3 = new SchemaBuilder(catalog, dialect);
            v3.BuildVersion3();
            migrations.Add(new SchemaMigration(3, "Resources: resources, settings, secret slot bindings, schema cache, and resource knowledge with versions", v3._Statements, v3._Columns));

            SchemaBuilder v4 = new SchemaBuilder(catalog, dialect);
            v4.BuildVersion4();
            migrations.Add(new SchemaMigration(4, "Tool invocations, approvals, and connector plugins", v4._Statements, v4._Columns));

            SchemaBuilder v5 = new SchemaBuilder(catalog, dialect);
            v5.BuildVersion5();
            migrations.Add(new SchemaMigration(5, "Agent profiles, bindings, binding tool rules, binding deny patterns, and prompt template layer hashes", v5._Statements, v5._Columns));

            SchemaBuilder v6 = new SchemaBuilder(catalog, dialect);
            v6.BuildVersion6();
            migrations.Add(new SchemaMigration(6, "Model endpoints", v6._Statements, v6._Columns));

            SchemaBuilder v7 = new SchemaBuilder(catalog, dialect);
            v7.BuildVersion7();
            migrations.Add(new SchemaMigration(7, "Conversations, messages, message tool calls, A2A tasks, and A2A task artifacts", v7._Statements, v7._Columns));

            SchemaBuilder v8 = new SchemaBuilder(catalog, dialect);
            v8.BuildVersion8();
            migrations.Add(new SchemaMigration(8, "Agent profile instruction detail (compact connector instructions)", v8._Statements, v8._Columns));

            SchemaBuilder v9 = new SchemaBuilder(catalog, dialect);
            v9.BuildVersion9();
            migrations.Add(new SchemaMigration(9, "Credential owner bypass opt-in, model endpoint health history, and the _root knowledge key", v9._Statements, v9._Columns));

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

        private void BuildVersion3()
        {
            SqlDialect d = _Dialect;

            // resources: searchable fields are columns; settings and secret slots are child tables (D-016)
            Table("resources",
                Id(),
                Ref("tenant_id", true),
                Ref("owner_user_id", false),
                "visibility VARCHAR(32) NOT NULL",
                "name VARCHAR(64) NOT NULL",
                "connector_type VARCHAR(64) NOT NULL",
                "display_name VARCHAR(256)",
                Text("description", false),
                "endpoint VARCHAR(512)",
                "environment VARCHAR(64)",
                Text("notes", false),
                "default_access_mode_ceiling VARCHAR(32)",
                Timestamp("last_test_utc", false),
                "last_test_result " + d.TypeBool,
                Text("last_test_message", false),
                Bool("active"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("resources", "tenant", false, "tenant_id");
            Index("resources", "name", true, "tenant_id", "name");
            Index("resources", "connector", false, "tenant_id", "connector_type");

            Table("resource_settings",
                Id(),
                Ref("resource_id", true),
                Ref("tenant_id", true),
                "setting_key VARCHAR(128) NOT NULL",
                "value_kind VARCHAR(32) NOT NULL",
                Text("string_value", false),
                "integer_value " + d.TypeBigInt,
                "boolean_value " + d.TypeBool);
            Index("resource_settings", "parent", true, "resource_id", "setting_key");
            Index("resource_settings", "tenant", false, "tenant_id");

            Table("resource_secret_bindings",
                Id(),
                Ref("resource_id", true),
                Ref("tenant_id", true),
                "slot_name VARCHAR(128) NOT NULL",
                Ref("secret_id", true),
                "secret_field_name VARCHAR(128)");
            Index("resource_secret_bindings", "parent", true, "resource_id", "slot_name");
            Index("resource_secret_bindings", "secret", false, "tenant_id", "secret_id");

            // connector_schema_cache: rebuildable connector caches (for example a SQL schema crawl)
            Table("connector_schema_cache",
                Id(),
                Ref("tenant_id", true),
                Ref("resource_id", true),
                "cache_kind VARCHAR(64) NOT NULL",
                Text("content", true),
                Timestamp("expires_utc", true),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("connector_schema_cache", "resourcekind", true, "resource_id", "cache_kind");
            Index("connector_schema_cache", "tenant", false, "tenant_id");

            // resource_knowledge: durable, scrubbed knowledge per resource (D-038), with history
            Table("resource_knowledge",
                Id(),
                Ref("tenant_id", true),
                Ref("resource_id", true),
                "scope VARCHAR(128) NOT NULL",
                "knowledge_key VARCHAR(512) NOT NULL",
                Text("content", true),
                "source VARCHAR(32) NOT NULL",
                "updated_by VARCHAR(256)",
                "version " + d.TypeInt + " NOT NULL",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("resource_knowledge", "entry", true, "resource_id", "scope", "knowledge_key");
            Index("resource_knowledge", "tenant", false, "tenant_id");

            Table("resource_knowledge_versions",
                Id(),
                Ref("tenant_id", true),
                Ref("knowledge_id", true),
                Ref("resource_id", true),
                "scope VARCHAR(128) NOT NULL",
                "knowledge_key VARCHAR(512) NOT NULL",
                "version " + d.TypeInt + " NOT NULL",
                Text("content", true),
                "source VARCHAR(32) NOT NULL",
                "updated_by VARCHAR(256)",
                Bool("deleted"),
                Timestamp("created_utc", true));
            Index("resource_knowledge_versions", "entry", false, "resource_id", "scope", "knowledge_key", "version");
            Index("resource_knowledge_versions", "tenant", false, "tenant_id");
        }

        private void BuildVersion4()
        {
            SqlDialect d = _Dialect;

            // tool_invocations: one row per call through the pipeline; arguments_redacted is unmanaged JSON (section 7.6)
            Table("tool_invocations",
                Id(),
                Ref("tenant_id", true),
                Ref("conversation_id", false),
                Ref("message_id", false),
                "surface VARCHAR(32) NOT NULL",
                "principal_type VARCHAR(32) NOT NULL",
                Ref("principal_id", false),
                "principal_name VARCHAR(256)",
                Ref("agent_profile_id", false),
                Ref("binding_id", false),
                "binding_alias VARCHAR(128)",
                Ref("resource_id", false),
                "connector_type VARCHAR(64)",
                "tool_name VARCHAR(128) NOT NULL",
                Text("arguments_redacted", false),
                Bool("redaction_applied"),
                "effect VARCHAR(32)",
                Text("effect_reason", false),
                "access_mode VARCHAR(32)",
                "decision VARCHAR(32) NOT NULL",
                "decision_code VARCHAR(64)",
                Text("decision_reason", false),
                Ref("approval_id", false),
                "status VARCHAR(32) NOT NULL",
                "error_code VARCHAR(64)",
                Text("error_message", false),
                "output_bytes " + d.TypeBigInt + " NOT NULL",
                Bool("output_truncated"),
                Text("output_preview", false),
                "duration_ms " + d.TypeBigInt + " NOT NULL",
                "trace_id VARCHAR(64)",
                "correlation_id VARCHAR(64)",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("tool_invocations", "tenantcreated", false, "tenant_id", "created_utc");
            Index("tool_invocations", "profile", false, "tenant_id", "agent_profile_id");
            Index("tool_invocations", "resource", false, "tenant_id", "resource_id");
            Index("tool_invocations", "approval", false, "approval_id");

            // approvals: database-backed so a decision survives restarts (D-027)
            Table("approvals",
                Id(),
                Ref("tenant_id", true),
                Ref("tool_invocation_id", true),
                Ref("agent_profile_id", false),
                Ref("binding_id", false),
                "binding_alias VARCHAR(128)",
                Ref("resource_id", false),
                "tool_name VARCHAR(128) NOT NULL",
                "surface VARCHAR(32) NOT NULL",
                "requested_by_principal_type VARCHAR(32) NOT NULL",
                Ref("requested_by_principal_id", false),
                "requested_by_name VARCHAR(256)",
                Text("summary", true),
                "effect VARCHAR(32) NOT NULL",
                Text("effect_reason", false),
                Text("arguments_redacted", false),
                "status VARCHAR(32) NOT NULL",
                Ref("decided_by_user_id", false),
                "decided_by_name VARCHAR(256)",
                Text("decision_note", false),
                Timestamp("decided_utc", false),
                Timestamp("expires_utc", true),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("approvals", "tenantstatus", false, "tenant_id", "status");
            Index("approvals", "expires", false, "status", "expires_utc");
            Index("approvals", "invocation", false, "tool_invocation_id");

            // plugins: global; system administrators manage plugins
            Table("plugins",
                Id(),
                "name VARCHAR(128) NOT NULL",
                "version VARCHAR(64)",
                Text("assembly_path", true),
                "sha256 VARCHAR(128)",
                Bool("enabled"),
                Bool("loaded"),
                Text("load_error", false),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("plugins", "name", true, "name");
            Table("plugin_connector_types",
                Id(),
                Ref("plugin_id", true),
                "connector_type VARCHAR(64) NOT NULL");
            Index("plugin_connector_types", "parent", false, "plugin_id");
        }

        private void BuildVersion5()
        {
            SqlDialect d = _Dialect;

            Table("agent_profiles",
                Id(),
                Ref("tenant_id", true),
                Ref("owner_user_id", false),
                "visibility VARCHAR(32) NOT NULL",
                "name VARCHAR(64) NOT NULL",
                "display_name VARCHAR(256)",
                "persona_name VARCHAR(128) NOT NULL",
                "user_address VARCHAR(128) NOT NULL",
                Text("description", false),
                "persona_template_key VARCHAR(128) NOT NULL",
                Ref("inference_endpoint_id", false),
                Ref("fallback_inference_endpoint_id", false),
                Ref("embedding_endpoint_id", false),
                "max_iterations " + d.TypeInt + " NOT NULL",
                "max_tool_calls_per_turn " + d.TypeInt + " NOT NULL",
                "tool_timeout_seconds " + d.TypeInt + " NOT NULL",
                "temperature " + d.TypeDouble,
                Bool("expose_a2a"),
                Bool("expose_mcp"),
                Bool("expose_openai"),
                Bool("expose_management_tools"),
                Text("instructions_append", false),
                Bool("is_default"),
                Bool("active"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("agent_profiles", "tenant", false, "tenant_id");
            Index("agent_profiles", "name", true, "tenant_id", "name");

            Table("bindings",
                Id(),
                Ref("tenant_id", true),
                Ref("agent_profile_id", true),
                Ref("resource_id", true),
                "alias VARCHAR(128) NOT NULL",
                "access_mode VARCHAR(32) NOT NULL",
                Bool("require_approval_for_destructive"),
                "instruction_mode VARCHAR(32) NOT NULL",
                Text("instruction_text", false),
                "max_output_bytes " + d.TypeInt,
                "max_rows " + d.TypeInt,
                Bool("enabled"),
                Bool("allow_knowledge_updates"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("bindings", "alias", true, "agent_profile_id", "alias");
            Index("bindings", "tenant", false, "tenant_id");
            Index("bindings", "resource", false, "tenant_id", "resource_id");

            Table("binding_tool_rules",
                Id(),
                Ref("binding_id", true),
                Ref("tenant_id", true),
                "tool_name VARCHAR(128) NOT NULL",
                "rule VARCHAR(32) NOT NULL");
            Index("binding_tool_rules", "parent", true, "binding_id", "tool_name");
            Index("binding_tool_rules", "tenant", false, "tenant_id");

            Table("binding_deny_patterns",
                Id(),
                Ref("binding_id", true),
                Ref("tenant_id", true),
                "ordinal " + d.TypeInt + " NOT NULL",
                Text("pattern", true));
            Index("binding_deny_patterns", "parent", false, "binding_id");
            Index("binding_deny_patterns", "tenant", false, "tenant_id");

            // Prompt template layers (D-015, D-039): the hash of the content of the layer below at the time this row was
            // last synchronized, so "the default changed since you customized this" can be detected.
            _Columns.Add(new SchemaColumnAddition("prompt_templates", "base_hash", "VARCHAR(128)"));
        }

        private void BuildVersion7()
        {
            SqlDialect d = _Dialect;

            // conversations (plan 7.5): one per dashboard chat, A2A context, OpenAI-compatible session, or delegated ask
            Table("conversations",
                Id(),
                Ref("tenant_id", true),
                Ref("user_id", false),
                Ref("credential_id", false),
                Ref("agent_profile_id", true),
                "title VARCHAR(256)",
                "source VARCHAR(32) NOT NULL",
                "a2a_context_id VARCHAR(128)",
                "external_key VARCHAR(256)",
                "message_count " + d.TypeInt + " NOT NULL",
                Timestamp("last_message_utc", false),
                "total_input_tokens " + d.TypeBigInt + " NOT NULL",
                "total_output_tokens " + d.TypeBigInt + " NOT NULL",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("conversations", "tenantupdated", false, "tenant_id", "last_update_utc");
            Index("conversations", "user", false, "tenant_id", "user_id");
            Index("conversations", "profile", false, "tenant_id", "agent_profile_id");
            Index("conversations", "context", false, "tenant_id", "agent_profile_id", "a2a_context_id");
            Index("conversations", "external", false, "tenant_id", "agent_profile_id", "external_key");

            // messages: role is User, Assistant, Tool, Summary (compaction), or System; compacted rows stay for export
            Table("messages",
                Id(),
                Ref("tenant_id", true),
                Ref("conversation_id", true),
                "turn_id VARCHAR(64)",
                "sequence " + d.TypeBigInt + " NOT NULL",
                "role VARCHAR(32) NOT NULL",
                Text("content", true),
                Text("thinking_content", false),
                "tool_call_id VARCHAR(128)",
                "tool_name VARCHAR(128)",
                Ref("model_endpoint_id", false),
                "input_tokens " + d.TypeBigInt + " NOT NULL",
                "output_tokens " + d.TypeBigInt + " NOT NULL",
                "time_to_first_token_ms " + d.TypeBigInt,
                "duration_ms " + d.TypeBigInt,
                "finish_reason VARCHAR(32)",
                Bool("compacted"),
                Timestamp("created_utc", true));
            Index("messages", "sequence", true, "conversation_id", "sequence");
            Index("messages", "tenantcreated", false, "tenant_id", "created_utc");

            // message_tool_calls: the calls an assistant message requested (child of messages)
            Table("message_tool_calls",
                Id(),
                Ref("message_id", true),
                Ref("conversation_id", true),
                Ref("tenant_id", true),
                "ordinal " + d.TypeInt + " NOT NULL",
                "call_id VARCHAR(128) NOT NULL",
                "name VARCHAR(128) NOT NULL",
                Text("arguments_json", true));
            Index("message_tool_calls", "parent", false, "message_id");
            Index("message_tool_calls", "conversation", false, "conversation_id");

            // a2a_tasks (D-006, D-027): persisted with principal ownership
            Table("a2a_tasks",
                Id(),
                Ref("tenant_id", true),
                Ref("agent_profile_id", true),
                "principal_type VARCHAR(32) NOT NULL",
                Ref("principal_id", false),
                Ref("conversation_id", true),
                "context_id VARCHAR(128) NOT NULL",
                "message_id VARCHAR(128)",
                "state VARCHAR(32) NOT NULL",
                Text("status_message", false),
                Ref("pending_approval_id", false),
                "input_tokens " + d.TypeBigInt + " NOT NULL",
                "output_tokens " + d.TypeBigInt + " NOT NULL",
                "tool_call_count " + d.TypeInt + " NOT NULL",
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("a2a_tasks", "profile", false, "tenant_id", "agent_profile_id", "created_utc");
            Index("a2a_tasks", "context", false, "tenant_id", "context_id");
            Index("a2a_tasks", "state", false, "state");

            // a2a_task_artifacts: content is unmanaged (plan 7.6)
            Table("a2a_task_artifacts",
                Id(),
                Ref("task_id", true),
                Ref("tenant_id", true),
                "artifact_id VARCHAR(128) NOT NULL",
                "name VARCHAR(256)",
                "mime_type VARCHAR(128) NOT NULL",
                Text("content", true),
                Timestamp("created_utc", true));
            Index("a2a_task_artifacts", "parent", false, "task_id");
        }

        private void BuildVersion8()
        {
            _Columns.Add(new SchemaColumnAddition("agent_profiles", "instruction_detail", "VARCHAR(16)"));
        }

        private void BuildVersion9()
        {
            SqlDialect d = _Dialect;

            // A credential carries its owner's administrator bypass only when a tenant or system administrator opts in.
            _Columns.Add(new SchemaColumnAddition("credentials", "inherit_owner_bypass", d.TypeBool + " NOT NULL DEFAULT " + d.BooleanLiteral(false)));

            // model_endpoint_health: recent health checks per endpoint (sweep and validate), pruned on insert
            Table("model_endpoint_health",
                Id(),
                Ref("tenant_id", true),
                Ref("model_endpoint_id", true),
                "status VARCHAR(32) NOT NULL",
                "source VARCHAR(32) NOT NULL",
                "latency_ms " + d.TypeBigInt,
                Text("message", false),
                Timestamp("checked_utc", true));
            Index("model_endpoint_health", "endpoint", false, "tenant_id", "model_endpoint_id", "checked_utc");

            // Knowledge scope roots are stored with the key _root (they were stored with an empty key). A stray literal
            // _root row that collides with an empty-key root is the duplicate and is removed first.
            string knowledge = _Catalog.Reference("resource_knowledge");
            string versions = _Catalog.Reference("resource_knowledge_versions");
            _Statements.Add("DELETE FROM " + knowledge + " WHERE knowledge_key = '_root' AND EXISTS (SELECT 1 FROM " + knowledge + " r2 WHERE r2.resource_id = " + knowledge + ".resource_id AND r2.scope = " + knowledge + ".scope AND r2.knowledge_key = '');");
            _Statements.Add("UPDATE " + knowledge + " SET knowledge_key = '_root' WHERE knowledge_key = '';");
            _Statements.Add("UPDATE " + versions + " SET knowledge_key = '_root' WHERE knowledge_key = '';");
        }

        private void BuildVersion6()
        {
            SqlDialect d = _Dialect;

            Table("model_endpoints",
                Id(),
                Ref("tenant_id", true),
                "name VARCHAR(128) NOT NULL",
                "kind VARCHAR(32) NOT NULL",
                "provider VARCHAR(32) NOT NULL",
                "base_url VARCHAR(1024)",
                "model VARCHAR(256) NOT NULL",
                Ref("api_key_secret_id", false),
                "api_key_secret_field VARCHAR(128)",
                "region VARCHAR(64)",
                "deployment_name VARCHAR(256)",
                "api_version VARCHAR(64)",
                "context_window_tokens " + d.TypeInt + " NOT NULL",
                "max_output_tokens " + d.TypeInt + " NOT NULL",
                Bool("supports_tools"),
                Bool("supports_streaming"),
                "embedding_dimensions " + d.TypeInt,
                "max_concurrency " + d.TypeInt + " NOT NULL",
                "timeout_seconds " + d.TypeInt + " NOT NULL",
                "max_retries " + d.TypeInt + " NOT NULL",
                "health_status VARCHAR(32) NOT NULL",
                Timestamp("last_health_utc", false),
                Text("last_health_message", false),
                Bool("active"),
                Timestamp("created_utc", true),
                Timestamp("last_update_utc", true));
            Index("model_endpoints", "tenant", false, "tenant_id");
            Index("model_endpoints", "name", true, "tenant_id", "name");
            Index("model_endpoints", "secret", false, "tenant_id", "api_key_secret_id");
        }

        #endregion
    }
}
