namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Sql;
    using Hugo.Core.Database.Telemetry;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Shared ADO.NET database driver. Implements the data-access surface once against
    /// <see cref="System.Data.Common"/>, parameterized by a <see cref="SqlDialect"/> and a <see cref="TableCatalog"/>.
    /// Provider subclasses supply a connection, a dialect, and connection or transaction specifics. Every statement and
    /// transaction runs through the execution methods here, which record its duration, errors, and span through
    /// <see cref="DatabaseTelemetry"/> (P13.24). Thread-safe.
    /// </summary>
    public abstract class AdoDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <summary>
        /// Provider dialect.
        /// </summary>
        public SqlDialect Dialect
        {
            get
            {
                return _Dialect;
            }
        }

        /// <summary>
        /// Table catalog.
        /// </summary>
        public TableCatalog Catalog
        {
            get
            {
                return _Catalog;
            }
        }

        /// <summary>
        /// Database settings.
        /// </summary>
        public DatabaseSettings Settings
        {
            get
            {
                return _Settings;
            }
        }

        /// <summary>
        /// Maximum number of retries when a transaction hits a transient conflict. Minimum 0, maximum 100, default 16.
        /// </summary>
        public int MaxConflictRetries
        {
            get
            {
                return _MaxConflictRetries;
            }
            set
            {
                _MaxConflictRetries = Math.Clamp(value, 0, 100);
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseSettings _Settings;
        private readonly SqlDialect _Dialect;
        private readonly TableCatalog _Catalog;
        private int _MaxConflictRetries = 16;
        private readonly string _Provider;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and wire the shared method implementations.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="dialect">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        protected AdoDatabaseDriver(DatabaseSettings settings, SqlDialect dialect)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            _Catalog = new TableCatalog(settings.TablePrefix, settings.Schema, dialect);
            _Provider = settings.Type.ToString().ToLowerInvariant();

            Tenants = new AdoTenantMethods(this);
            Users = new AdoUserMethods(this);
            Credentials = new AdoCredentialMethods(this);
            Sessions = new AdoAuthSessionMethods(this);
            Roles = new AdoRoleMethods(this);
            Permissions = new AdoPermissionMethods(this);
            RolePermissionMaps = new AdoRolePermissionMapMethods(this);
            UserRoleAssignments = new AdoUserRoleAssignmentMethods(this);
            CredentialScopeAssignments = new AdoCredentialScopeAssignmentMethods(this);
            Audit = new AdoAuditRecordMethods(this);
            RequestHistory = new AdoRequestHistoryEntryMethods(this);
            PromptTemplates = new AdoPromptTemplateMethods(this);
            PromptTemplateVersions = new AdoPromptTemplateVersionMethods(this);
            Secrets = new AdoSecretMethods(this);
            ClusterSettings = new AdoClusterSettingMethods(this);
            Nonces = new AdoRequestNonceMethods(this);
            RateLimits = new AdoRateLimitBucketMethods(this);
            MasterKeys = new AdoMasterKeyVersionMethods(this);
            Resources = new AdoResourceMethods(this);
            AgentProfiles = new AdoAgentProfileMethods(this);
            Bindings = new AdoBindingMethods(this);
            ToolInvocations = new AdoToolInvocationMethods(this);
            Approvals = new AdoApprovalMethods(this);
            ResourceKnowledge = new AdoResourceKnowledgeMethods(this);
            ModelEndpoints = new AdoModelEndpointMethods(this);
            ModelEndpointHealth = new AdoModelEndpointHealthMethods(this);
            Plugins = new AdoPluginMethods(this);
            SchemaCache = new AdoConnectorSchemaCacheMethods(this);
            Conversations = new AdoConversationMethods(this);
            Messages = new AdoMessageMethods(this);
            A2ATasks = new AdoA2ATaskMethods(this);
            Analytics = new AdoAnalyticsMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            _Settings.Validate();

            // Replicas starting together wait here; the first applies the migrations and the rest find nothing to do
            // (P10.9). The lock also covers schema creation, which Postgresql does not make safe under concurrency.
            await using (DatabaseLockHandle startupLock = await AcquireStartupLockAsync(token).ConfigureAwait(false))
            {
                if (!String.IsNullOrEmpty(_Settings.Schema))
                {
                    string? schemaSql = _Dialect.CreateSchemaStatement(_Settings.Schema!);
                    if (!String.IsNullOrEmpty(schemaSql)) await NonQueryAsync(schemaSql!, null, token).ConfigureAwait(false);
                }

                if (_Settings.ManageSchema) await ApplyMigrationsAsync(token).ConfigureAwait(false);
                else await VerifyTablesAsync(token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override async Task<int> ReadSchemaVersionAsync(CancellationToken token = default)
        {
            object? result = await ScalarAsync("SELECT MAX(version) FROM " + _Catalog.Reference("schema_migrations") + ";", null, token).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToInt32(result);
        }

        /// <summary>
        /// A stable 64-bit key for a lock name, for database locks keyed by integers (Postgresql advisory locks).
        /// </summary>
        /// <param name="name">Lock name.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        public static long LockKey(string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name));
            return BitConverter.ToInt64(hash, 0);
        }

        /// <summary>
        /// Name of the startup lock for this database, schema, and table prefix.
        /// </summary>
        /// <returns>Lock name.</returns>
        public string StartupLockName()
        {
            return "hugo-startup:" + (_Catalog.Schema ?? String.Empty) + ":" + _Catalog.Prefix;
        }

        /// <inheritdoc />
        public override async Task<bool> PingAsync(CancellationToken token = default)
        {
            object? result = await ScalarAsync("SELECT 1;", null, token).ConfigureAwait(false);
            return result != null;
        }

        /// <inheritdoc />
        public override Task CloseAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolve a raw table name to a quoted, prefixed, optionally schema-qualified reference.
        /// </summary>
        /// <param name="rawName">Unprefixed table name.</param>
        /// <returns>Table reference.</returns>
        public string Table(string rawName)
        {
            return _Catalog.Reference(rawName);
        }

        /// <summary>
        /// Create a new, unopened connection.
        /// </summary>
        /// <returns>Connection.</returns>
        public abstract DbConnection CreateConnection();

        /// <summary>
        /// Open a new connection.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Open connection.</returns>
        public virtual async Task<DbConnection> OpenConnectionAsync(CancellationToken token = default)
        {
            DbConnection connection = CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            DatabaseTelemetry.TrackConnection(connection);
            await OnConnectionOpenedAsync(connection, token).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Begin a transaction on an open connection.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Transaction.</returns>
        /// <exception cref="ArgumentNullException">Thrown when connection is null.</exception>
        public virtual async Task<DbTransaction> BeginTransactionAsync(DbConnection connection, CancellationToken token = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            return await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute a non-query statement.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Affected rows.</returns>
        public async Task<int> NonQueryAsync(string sql, Action<DbCommand>? bind, CancellationToken token = default)
        {
            DatabaseStatement statement = DatabaseTelemetry.Describe(sql);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, _Provider);
            try
            {
                using (IDisposable? writeLock = await EnterWriteTimedAsync(token).ConfigureAwait(false))
                {
                    await using (DbConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false))
                    {
                        await using (DbCommand command = connection.CreateCommand())
                        {
                            command.CommandText = sql;
                            bind?.Invoke(command);
                            int affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            DatabaseTelemetry.End(activity, statement, _Provider, start, null);
                            return affected;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, _Provider, start, e);
                throw;
            }
        }

        /// <summary>
        /// Execute a scalar query.
        /// </summary>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Scalar value, or null.</returns>
        public async Task<object?> ScalarAsync(string sql, Action<DbCommand>? bind, CancellationToken token = default)
        {
            DatabaseStatement statement = DatabaseTelemetry.Describe(sql);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, _Provider);
            try
            {
                await using (DbConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false))
                {
                    await using (DbCommand command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        bind?.Invoke(command);
                        object? result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                        DatabaseTelemetry.End(activity, statement, _Provider, start, null);
                        return result == null || result == DBNull.Value ? null : result;
                    }
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, _Provider, start, e);
                throw;
            }
        }

        /// <summary>
        /// Execute a query and map each row.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mapped rows.</returns>
        /// <exception cref="ArgumentNullException">Thrown when map is null.</exception>
        public async Task<List<T>> QueryAsync<T>(string sql, Action<DbCommand>? bind, Func<DbDataReader, T> map, CancellationToken token = default)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            List<T> results = new List<T>();
            DatabaseStatement statement = DatabaseTelemetry.Describe(sql);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, _Provider);
            try
            {
                await using (DbConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false))
                {
                    await using (DbCommand command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        bind?.Invoke(command);
                        await using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(map(reader));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, _Provider, start, e);
                throw;
            }
            activity?.SetTag("db.response.returned_rows", results.Count);
            DatabaseTelemetry.End(activity, statement, _Provider, start, null);
            return results;
        }

        /// <summary>
        /// Execute a query and map the first row.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>First row, or null.</returns>
        public async Task<T?> QuerySingleAsync<T>(string sql, Action<DbCommand>? bind, Func<DbDataReader, T> map, CancellationToken token = default) where T : class
        {
            List<T> results = await QueryAsync(sql, bind, map, token).ConfigureAwait(false);
            return results.Count > 0 ? results[0] : null;
        }

        /// <summary>
        /// Run work inside one transaction, retrying on transient conflicts. The transaction commits when the
        /// work completes and rolls back when it throws.
        /// </summary>
        /// <param name="work">Work receiving the connection, transaction, and token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when work is null.</exception>
        public async Task ExecuteInTransactionAsync(Func<DbConnection, DbTransaction, CancellationToken, Task> work, CancellationToken token = default)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            int attempt = 0;
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(DatabaseTelemetry.Transaction, _Provider);
            try
            {
                while (true)
                {
                    try
                    {
                        using (IDisposable? writeLock = await EnterWriteTimedAsync(token).ConfigureAwait(false))
                        {
                            await using (DbConnection connection = await OpenConnectionAsync(token).ConfigureAwait(false))
                            {
                                await using (DbTransaction transaction = await BeginTransactionAsync(connection, token).ConfigureAwait(false))
                                {
                                    await work(connection, transaction, token).ConfigureAwait(false);
                                    await transaction.CommitAsync(token).ConfigureAwait(false);
                                    activity?.SetTag("hugo.db.retries", attempt);
                                    DatabaseTelemetry.End(activity, DatabaseTelemetry.Transaction, _Provider, start, null);
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (_Dialect.IsTransientConflict(ex) && attempt < _MaxConflictRetries)
                    {
                        attempt++;
                        DatabaseTelemetry.Retried(_Provider);
                        await Task.Delay(Math.Min(5 + attempt * 5, 100), token).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, DatabaseTelemetry.Transaction, _Provider, start, e);
                throw;
            }
        }

        /// <summary>
        /// Execute a statement on an existing connection and transaction.
        /// </summary>
        /// <param name="connection">Connection.</param>
        /// <param name="transaction">Transaction.</param>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Affected rows.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<int> ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, Action<DbCommand>? bind, CancellationToken token = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (String.IsNullOrEmpty(sql)) throw new ArgumentNullException(nameof(sql));

            DatabaseStatement statement = DatabaseTelemetry.Describe(sql);
            string provider = DatabaseTelemetry.Provider(connection);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, provider);
            try
            {
                await using (DbCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    bind?.Invoke(command);
                    int affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    DatabaseTelemetry.End(activity, statement, provider, start, null);
                    return affected;
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, provider, start, e);
                throw;
            }
        }

        /// <summary>
        /// Execute a reusable command without disposing it, retaining statement telemetry for every execution.
        /// The caller owns the command, connection, transaction, parameter values, and preparation.
        /// </summary>
        /// <param name="command">Configured command.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Affected rows.</returns>
        /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
        public static async Task<int> ExecuteAsync(DbCommand command, CancellationToken token = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            DatabaseStatement statement = DatabaseTelemetry.Describe(command.CommandText);
            string provider = DatabaseTelemetry.Provider(command.Connection);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, provider);
            try
            {
                int affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                DatabaseTelemetry.End(activity, statement, provider, start, null);
                return affected;
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, provider, start, e);
                throw;
            }
        }

        /// <summary>
        /// Execute a query on an existing connection and transaction and map each row.
        /// </summary>
        /// <typeparam name="T">Row type.</typeparam>
        /// <param name="connection">Connection.</param>
        /// <param name="transaction">Transaction.</param>
        /// <param name="sql">SQL text.</param>
        /// <param name="bind">Optional parameter binder.</param>
        /// <param name="map">Row mapper.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Mapped rows.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<List<T>> QueryAsync<T>(DbConnection connection, DbTransaction transaction, string sql, Action<DbCommand>? bind, Func<DbDataReader, T> map, CancellationToken token = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (map == null) throw new ArgumentNullException(nameof(map));

            List<T> results = new List<T>();
            DatabaseStatement statement = DatabaseTelemetry.Describe(sql);
            string provider = DatabaseTelemetry.Provider(connection);
            long start = Stopwatch.GetTimestamp();
            Activity? activity = DatabaseTelemetry.Start(statement, provider);
            try
            {
                await using (DbCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    bind?.Invoke(command);
                    await using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false)) results.Add(map(reader));
                    }
                }
            }
            catch (Exception e)
            {
                DatabaseTelemetry.End(activity, statement, provider, start, e);
                throw;
            }
            DatabaseTelemetry.End(activity, statement, provider, start, null);
            return results;
        }

        /// <summary>
        /// Add a parameter, mapping null to DBNull and normalizing timestamps to UTC.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <param name="name">Parameter name without prefix.</param>
        /// <param name="value">Value, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when command or name is null.</exception>
        public static void Add(DbCommand command, string name, object? value)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (value is DateTime dt) value = TimestampHelper.Normalize(dt);

            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "@" + name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Hook invoked after a connection opens. Override to set provider pragmas.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Enter the provider's write section. Providers that need writes serialized (Sqlite) return a releaser;
        /// others return null.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Releaser, or null.</returns>
        protected virtual Task<IDisposable?> EnterWriteAsync(CancellationToken token)
        {
            return Task.FromResult<IDisposable?>(null);
        }

        #endregion

        #region Private-Methods

        private async Task<IDisposable?> EnterWriteTimedAsync(CancellationToken token)
        {
            long start = Stopwatch.GetTimestamp();
            IDisposable? writeLock = await EnterWriteAsync(token).ConfigureAwait(false);
            if (writeLock != null) DatabaseTelemetry.WriteLockWaited(_Provider, start);
            return writeLock;
        }

        private async Task ApplyMigrationsAsync(CancellationToken token)
        {
            await NonQueryAsync(SchemaBuilder.MigrationsTableDdl(_Catalog, _Dialect), null, token).ConfigureAwait(false);

            HashSet<int> applied = new HashSet<int>();
            List<int> appliedList = await QueryAsync(
                "SELECT version FROM " + _Catalog.Reference("schema_migrations") + " ORDER BY version ASC;",
                null,
                reader => Convert.ToInt32(reader.GetValue(0)),
                token).ConfigureAwait(false);
            foreach (int version in appliedList) applied.Add(version);

            foreach (SchemaMigration migration in SchemaBuilder.Build(_Catalog, _Dialect))
            {
                if (applied.Contains(migration.Version)) continue;

                await ExecuteInTransactionAsync(async (connection, transaction, ct) =>
                {
                    foreach (SchemaColumnAddition column in migration.Columns)
                    {
                        string table = _Catalog.RawName(column.Table);
                        List<int> present = await QueryAsync(connection, transaction, _Dialect.ColumnExistsProbe(_Catalog.Schema), command =>
                        {
                            Add(command, "table", table);
                            Add(command, "column", column.Column);
                            if (!String.IsNullOrEmpty(_Catalog.Schema)) Add(command, "schema", _Catalog.Schema);
                        }, reader => 1, ct).ConfigureAwait(false);
                        if (present.Count > 0) continue;
                        await ExecuteAsync(connection, transaction, _Dialect.AddColumn(_Catalog.Schema, table, column.Column, column.Definition), null, ct).ConfigureAwait(false);
                    }

                    foreach (string statement in migration.Statements)
                    {
                        await ExecuteAsync(connection, transaction, statement, null, ct).ConfigureAwait(false);
                    }

                    await ExecuteAsync(connection, transaction,
                        "INSERT INTO " + _Catalog.Reference("schema_migrations") + " (version, description, applied_utc) VALUES (@v, @d, @t);",
                        command =>
                        {
                            Add(command, "v", migration.Version);
                            Add(command, "d", migration.Description);
                            Add(command, "t", DateTime.UtcNow);
                        }, ct).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
                applied.Add(migration.Version);
                MigrationsApplied++;
            }

            SchemaVersion = applied.Count > 0 ? applied.Max() : 0;
        }

        private async Task VerifyTablesAsync(CancellationToken token)
        {
            foreach (string rawName in TableCatalog.AllTables)
            {
                string probe = _Dialect.TableExistsProbe(_Catalog.Schema);
                object? result = await ScalarAsync(probe, command =>
                {
                    Add(command, "name", _Catalog.RawName(rawName));
                    if (!String.IsNullOrEmpty(_Catalog.Schema)) Add(command, "schema", _Catalog.Schema);
                }, token).ConfigureAwait(false);

                if (result == null)
                {
                    throw new InvalidOperationException(
                        "Required table '" + _Catalog.RawName(rawName) + "' was not found and ManageSchema is disabled. " +
                        "Create the Hugo tables or enable ManageSchema.");
                }
            }
        }

        #endregion
    }
}
