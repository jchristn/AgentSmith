namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral model endpoint data access. Thread-safe.
    /// </summary>
    public class AdoModelEndpointMethods : IModelEndpointMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private static readonly string _Columns = "id, tenant_id, name, kind, provider, base_url, model, api_key_secret_id, api_key_secret_field, region, deployment_name, api_version, context_window_tokens, max_output_tokens, supports_tools, supports_streaming, embedding_dimensions, max_concurrency, timeout_seconds, max_retries, health_status, last_health_utc, last_health_message, active, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoModelEndpointMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("model_endpoints");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelEndpoint> CreateAsync(ModelEndpoint obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj, true), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND name = @name;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "name", name);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint> UpdateAsync(ModelEndpoint obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            List<string> sets = new List<string>();
            foreach (string column in _Columns.Split(", "))
            {
                if (column == "id" || column == "tenant_id" || column == "created_utc") continue;
                sets.Add(column + " = @" + column);
            }
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET " + String.Join(", ", sets) + " WHERE tenant_id = @tenant_id AND id = @id;", command => Bind(command, obj, false), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int affected = 0;
            string profiles = _Driver.Table("agent_profiles");
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                Action<DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "id", id);
                };
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + profiles + " SET inference_endpoint_id = NULL WHERE tenant_id = @tenant_id AND inference_endpoint_id = @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + profiles + " SET fallback_inference_endpoint_id = NULL WHERE tenant_id = @tenant_id AND fallback_inference_endpoint_id = @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + profiles + " SET embedding_endpoint_id = NULL WHERE tenant_id = @tenant_id AND embedding_endpoint_id = @id;", bind, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("name", "search"));
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<ModelEndpoint> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, true) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<ModelEndpoint>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> ListActiveAsync(CancellationToken token = default)
        {
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE active = " + _Driver.Dialect.BooleanLiteral(true) + " ORDER BY tenant_id ASC, name ASC;", null, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> ListBySecretAsync(string tenantId, string secretId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(secretId)) throw new ArgumentNullException(nameof(secretId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND api_key_secret_id = @secret_id ORDER BY name ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "secret_id", secretId);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpdateHealthAsync(string tenantId, string id, ModelHealthStatusEnum status, string? message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET health_status = @status, last_health_utc = @now, last_health_message = @message WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "status", status.ToString());
                AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                AdoDatabaseDriver.Add(command, "message", message);
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a model endpoint.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Endpoint.</returns>
        public static ModelEndpoint Map(DbDataReader reader)
        {
            ModelEndpoint obj = new ModelEndpoint();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.Kind = AdoConverters.Enum(reader, "kind", ModelKindEnum.Inference);
            obj.Provider = AdoConverters.Enum(reader, "provider", ModelProviderEnum.OpenAICompatible);
            obj.BaseUrl = AdoConverters.NullableStr(reader, "base_url");
            obj.Model = AdoConverters.Str(reader, "model");
            obj.ApiKeySecretId = AdoConverters.NullableStr(reader, "api_key_secret_id");
            obj.ApiKeySecretField = AdoConverters.NullableStr(reader, "api_key_secret_field");
            obj.Region = AdoConverters.NullableStr(reader, "region");
            obj.DeploymentName = AdoConverters.NullableStr(reader, "deployment_name");
            obj.ApiVersion = AdoConverters.NullableStr(reader, "api_version");
            obj.ContextWindowTokens = AdoConverters.Int(reader, "context_window_tokens");
            obj.MaxOutputTokens = AdoConverters.Int(reader, "max_output_tokens");
            obj.SupportsTools = AdoConverters.Bool(reader, "supports_tools");
            obj.SupportsStreaming = AdoConverters.Bool(reader, "supports_streaming");
            obj.EmbeddingDimensions = AdoConverters.NullableInt(reader, "embedding_dimensions");
            obj.MaxConcurrency = AdoConverters.Int(reader, "max_concurrency");
            obj.TimeoutSeconds = AdoConverters.Int(reader, "timeout_seconds");
            obj.MaxRetries = AdoConverters.Int(reader, "max_retries");
            obj.HealthStatus = AdoConverters.Enum(reader, "health_status", ModelHealthStatusEnum.Unknown);
            obj.LastHealthUtc = AdoConverters.NullableDate(reader, "last_health_utc");
            obj.LastHealthMessage = AdoConverters.NullableStr(reader, "last_health_message");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, ModelEndpoint obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "name", obj.Name);
            AdoDatabaseDriver.Add(command, "kind", obj.Kind.ToString());
            AdoDatabaseDriver.Add(command, "provider", obj.Provider.ToString());
            AdoDatabaseDriver.Add(command, "base_url", obj.BaseUrl);
            AdoDatabaseDriver.Add(command, "model", obj.Model);
            AdoDatabaseDriver.Add(command, "api_key_secret_id", obj.ApiKeySecretId);
            AdoDatabaseDriver.Add(command, "api_key_secret_field", obj.ApiKeySecretField);
            AdoDatabaseDriver.Add(command, "region", obj.Region);
            AdoDatabaseDriver.Add(command, "deployment_name", obj.DeploymentName);
            AdoDatabaseDriver.Add(command, "api_version", obj.ApiVersion);
            AdoDatabaseDriver.Add(command, "context_window_tokens", obj.ContextWindowTokens);
            AdoDatabaseDriver.Add(command, "max_output_tokens", obj.MaxOutputTokens);
            AdoDatabaseDriver.Add(command, "supports_tools", obj.SupportsTools);
            AdoDatabaseDriver.Add(command, "supports_streaming", obj.SupportsStreaming);
            AdoDatabaseDriver.Add(command, "embedding_dimensions", obj.EmbeddingDimensions);
            AdoDatabaseDriver.Add(command, "max_concurrency", obj.MaxConcurrency);
            AdoDatabaseDriver.Add(command, "timeout_seconds", obj.TimeoutSeconds);
            AdoDatabaseDriver.Add(command, "max_retries", obj.MaxRetries);
            AdoDatabaseDriver.Add(command, "health_status", obj.HealthStatus.ToString());
            AdoDatabaseDriver.Add(command, "last_health_utc", obj.LastHealthUtc);
            AdoDatabaseDriver.Add(command, "last_health_message", obj.LastHealthMessage);
            AdoDatabaseDriver.Add(command, "active", obj.Active);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
