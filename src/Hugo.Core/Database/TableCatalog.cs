namespace Hugo.Core.Database
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Database.Sql;

    /// <summary>
    /// Resolves raw table names to prefixed, quoted, optionally schema-qualified references. Thread-safe.
    /// </summary>
    public class TableCatalog
    {
        #region Public-Members

        /// <summary>
        /// Every data table in the schema, unprefixed.
        /// </summary>
        public static IReadOnlyList<string> AllTables
        {
            get
            {
                return _AllTables;
            }
        }

        /// <summary>
        /// Optional schema.
        /// </summary>
        public string? Schema { get; }

        /// <summary>
        /// Table name prefix.
        /// </summary>
        public string Prefix { get; }

        #endregion

        #region Private-Members

        private static readonly List<string> _AllTables = new List<string>
        {
            "tenants",
            "users",
            "credentials",
            "auth_sessions",
            "roles",
            "permissions",
            "permission_resource_types",
            "permission_operation_types",
            "role_permission_maps",
            "user_role_assignments",
            "credential_scope_assignments",
            "audit_records",
            "request_history",
            "request_history_headers",
            "prompt_templates",
            "prompt_template_variables",
            "prompt_template_versions",
            "cluster_settings",
            "request_nonces",
            "rate_limit_buckets",
            "secrets",
            "secret_values",
            "master_key_versions",
        };

        private readonly SqlDialect _Dialect;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="prefix">Table name prefix, or null for none.</param>
        /// <param name="schema">Optional schema.</param>
        /// <param name="dialect">Dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when dialect is null.</exception>
        public TableCatalog(string? prefix, string? schema, SqlDialect dialect)
        {
            _Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            Prefix = prefix ?? String.Empty;
            Schema = String.IsNullOrWhiteSpace(schema) ? null : schema;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Prefixed raw table name.
        /// </summary>
        /// <param name="rawName">Unprefixed name.</param>
        /// <returns>Prefixed name.</returns>
        public string RawName(string rawName)
        {
            if (String.IsNullOrEmpty(rawName)) throw new ArgumentNullException(nameof(rawName));
            return Prefix + rawName;
        }

        /// <summary>
        /// Quoted, prefixed, optionally schema-qualified reference.
        /// </summary>
        /// <param name="rawName">Unprefixed name.</param>
        /// <returns>Reference.</returns>
        public string Reference(string rawName)
        {
            return _Dialect.QualifiedReference(Schema, RawName(rawName));
        }

        #endregion
    }
}
