namespace Hugo.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql.Tools;

    /// <summary>
    /// Shared implementation of the four SQL connectors (family <c>sql</c>). Each connector instance owns its tools and
    /// an in-process schema cache. Thread-safe.
    /// </summary>
    public abstract class SqlConnectorBase : IConnector
    {
        #region Public-Members

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Dialect.
        /// </summary>
        public SqlDialect Dialect { get; }

        /// <summary>
        /// Full-crawl cache shared by the discovery, intelligence, context, and question tools.
        /// </summary>
        public SchemaCache SchemaCache { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dialect">Dialect.</param>
        /// <param name="type">Connector type (for example <c>postgresql</c>).</param>
        /// <param name="description">Human description.</param>
        /// <param name="documentationUrl">Upstream documentation URL.</param>
        /// <exception cref="ArgumentNullException">Thrown when dialect or type is null.</exception>
        protected SqlConnectorBase(SqlDialect dialect, string type, string description, string documentationUrl)
        {
            Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            SchemaCache = new SchemaCache();

            Descriptor = new ConnectorDescriptor
            {
                Type = type,
                Family = "sql",
                DisplayName = dialect.DisplayName,
                Description = description ?? String.Empty,
                Category = ConnectorCategoryEnum.Database,
                IconKey = "database",
                Version = "1.0.0",
                SettingsSchema = SqlSettingsCatalog.BuildSettings(dialect.Engine),
                SecretSlots = SqlSettingsCatalog.BuildSecretSlots(dialect.Engine),
                DefaultInstructionsKey = "connector." + type + ".instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(SqlConnectorBase).Assembly, "connector." + type + ".instructions.md"),
                DocumentationUrl = documentationUrl
            };

            Tools = new List<IConnectorTool>
            {
                new SqlListSchemasTool(dialect, SchemaCache),
                new SqlListTablesTool(dialect, SchemaCache),
                new SqlDescribeTableTool(dialect, SchemaCache),
                new SqlDescribeSchemaTool(dialect, SchemaCache),
                new SqlQueryTool(dialect, SchemaCache),
                new SqlExplainTool(dialect, SchemaCache),
                new SqlActivityTool(dialect, SchemaCache),
                new SqlTableSizesTool(dialect, SchemaCache),
                new SqlLocksTool(dialect, SchemaCache),
                new SqlGetAgentPackTool(dialect, SchemaCache),
                new SqlListRelationshipsTool(dialect, SchemaCache),
                new SqlGetIntelligenceTool(dialect, SchemaCache),
                new SqlGetContextTool(dialect, SchemaCache),
                new SqlUpdateContextTool(dialect, SchemaCache),
                new SqlGenerateContextTool(dialect, SchemaCache),
                new SqlAskTool(dialect, SchemaCache)
            };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();

            ConnectorValidationResult result = ConnectorValidationResult.Valid();
            SqlResourceOptions options = SqlResourceOptions.FromResource(resource);
            bool hasConnectionString = !String.IsNullOrWhiteSpace(options.ConnectionString);

            if (Dialect.Engine == SqlEngineEnum.Sqlite)
            {
                if (!hasConnectionString && String.IsNullOrWhiteSpace(options.Filename))
                    result.Add(SqlSettingKeys.Filename, "required", "Set filename or bind a connectionString secret.");
            }
            else if (!hasConnectionString && String.IsNullOrWhiteSpace(options.Host))
            {
                result.Add(SqlSettingKeys.Host, "required", "Set host or bind a connectionString secret.");
            }

            string? port = resource.Settings.GetString(SqlSettingKeys.Port);
            if (port != null && (!Int64.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) || p < 1 || p > 65535))
                result.Add(SqlSettingKeys.Port, "out_of_range", "Port must be between 1 and 65535.");

            string? enforcement = resource.Settings.GetString(SqlSettingKeys.ReadOnlyEnforcement);
            if (enforcement != null && !enforcement.Equals("Session", StringComparison.OrdinalIgnoreCase) && !enforcement.Equals("ClassifierOnly", StringComparison.OrdinalIgnoreCase))
                result.Add(SqlSettingKeys.ReadOnlyEnforcement, "invalid_value", "readOnlyEnforcement must be Session or ClassifierOnly.");

            if (hasConnectionString)
            {
                try
                {
                    Dialect.BuildConnectionString(options, false);
                }
                catch (Exception e) when (e is ArgumentException || e is FormatException || e is KeyNotFoundException || e is InvalidOperationException)
                {
                    result.Add(SqlSettingKeys.ConnectionStringSlot, "invalid_format", "The connection string is not valid for " + Dialect.DisplayName + ".");
                }
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            Stopwatch stopwatch = Stopwatch.StartNew();
            SqlResourceOptions options;
            try
            {
                options = SqlResourceOptions.FromResource(resource);
            }
            catch (ConnectorException e)
            {
                return ConnectorHealthResult.Fail(e.Code, e.Message, stopwatch.Elapsed.TotalMilliseconds);
            }

            int seconds = options.ConnectTimeoutSeconds + options.CommandTimeoutSeconds;
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    try
                    {
                        await using (SqlSession session = await SqlSession.OpenAsync(Dialect, options, true, linked.Token).ConfigureAwait(false))
                        {
                            string version;
                            using (DbCommand command = session.CreateCommand(Dialect.VersionSql, options.CommandTimeoutSeconds))
                            {
                                object? raw = await command.ExecuteScalarAsync(linked.Token).ConfigureAwait(false);
                                version = Dialect.FormatVersion(Convert.ToString(raw, CultureInfo.InvariantCulture) ?? String.Empty);
                            }
                            stopwatch.Stop();
                            ConnectorHealthResult ok = ConnectorHealthResult.Ok("Connected to " + Dialect.DisplayName + " " + version, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2));
                            ok.Details["serverVersion"] = version;
                            ok.Details["engine"] = Descriptor.Type;
                            ok.Details["database"] = session.Connection.Database ?? String.Empty;
                            ok.Details["defaultSchema"] = Dialect.DefaultSchema(options, session.Connection);
                            ok.Details["readOnlyEnforcement"] = session.EngineReadOnly ? "session" : "classifier";
                            return ok;
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        return ConnectorHealthResult.Fail("timeout", "Timed out after " + seconds + " seconds connecting to " + Dialect.DisplayName + " (" + e.GetType().Name + ").", stopwatch.Elapsed.TotalMilliseconds);
                    }
                    catch (ConnectorException e)
                    {
                        return ConnectorHealthResult.Fail(e.Code, e.Message, stopwatch.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception e) when (e is DbException || e is InvalidOperationException || e is ArgumentException)
                    {
                        return ConnectorHealthResult.Fail("connection_failed", Dialect.DisplayName + " probe failed: " + e.Message, stopwatch.Elapsed.TotalMilliseconds);
                    }
                }
            }
        }

        #endregion
    }
}
