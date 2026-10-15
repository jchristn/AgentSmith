namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral model endpoint health history. Each insert prunes the endpoint's records beyond the limit, so the
    /// table stays small. Thread-safe.
    /// </summary>
    public class AdoModelEndpointHealthMethods : IModelEndpointHealthMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoModelEndpointHealthMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("model_endpoint_health");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelEndpointHealthRecord> AddAsync(ModelEndpointHealthRecord record, int keep, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (String.IsNullOrEmpty(record.TenantId)) throw new ArgumentException("The record has no tenant.", nameof(record));
            if (String.IsNullOrEmpty(record.ModelEndpointId)) throw new ArgumentException("The record has no model endpoint.", nameof(record));
            int limit = Math.Clamp(keep, 1, 10000);
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Table + " (id, tenant_id, model_endpoint_id, status, source, latency_ms, message, checked_utc) VALUES (@id, @tenant_id, @model_endpoint_id, @status, @source, @latency_ms, @message, @checked_utc);", command =>
                {
                    AdoDatabaseDriver.Add(command, "id", record.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", record.TenantId);
                    AdoDatabaseDriver.Add(command, "model_endpoint_id", record.ModelEndpointId);
                    AdoDatabaseDriver.Add(command, "status", record.Status.ToString());
                    AdoDatabaseDriver.Add(command, "source", record.Source.ToString());
                    AdoDatabaseDriver.Add(command, "latency_ms", record.LatencyMs);
                    AdoDatabaseDriver.Add(command, "message", record.Message);
                    AdoDatabaseDriver.Add(command, "checked_utc", record.CheckedUtc);
                }, ct).ConfigureAwait(false);

                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND model_endpoint_id = @model_endpoint_id AND id NOT IN (SELECT id FROM " + _Table
                    + " WHERE tenant_id = @tenant_id AND model_endpoint_id = @model_endpoint_id ORDER BY checked_utc DESC, id DESC" + _Driver.Dialect.LimitOffsetClause() + ");", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", record.TenantId);
                    AdoDatabaseDriver.Add(command, "model_endpoint_id", record.ModelEndpointId);
                    AdoDatabaseDriver.Add(command, "max", limit);
                    AdoDatabaseDriver.Add(command, "skip", 0);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpointHealthRecord>> ListAsync(string tenantId, string modelEndpointId, int maxResults, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(modelEndpointId)) throw new ArgumentNullException(nameof(modelEndpointId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND model_endpoint_id = @model_endpoint_id ORDER BY checked_utc DESC, id DESC" + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "model_endpoint_id", modelEndpointId);
                AdoDatabaseDriver.Add(command, "max", Math.Clamp(maxResults, 1, 1000));
                AdoDatabaseDriver.Add(command, "skip", 0);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByEndpointAsync(string tenantId, string modelEndpointId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(modelEndpointId)) throw new ArgumentNullException(nameof(modelEndpointId));
            await _Driver.NonQueryAsync("DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND model_endpoint_id = @model_endpoint_id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "model_endpoint_id", modelEndpointId);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a health record.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Record.</returns>
        public static ModelEndpointHealthRecord Map(DbDataReader reader)
        {
            ModelEndpointHealthRecord record = new ModelEndpointHealthRecord();
            record.Id = AdoConverters.Str(reader, "id");
            record.TenantId = AdoConverters.Str(reader, "tenant_id");
            record.ModelEndpointId = AdoConverters.Str(reader, "model_endpoint_id");
            record.Status = AdoConverters.Enum(reader, "status", ModelHealthStatusEnum.Unknown);
            record.Source = AdoConverters.Enum(reader, "source", ModelHealthSourceEnum.Sweep);
            record.LatencyMs = AdoConverters.NullableLong(reader, "latency_ms");
            record.Message = AdoConverters.NullableStr(reader, "message");
            record.CheckedUtc = AdoConverters.Date(reader, "checked_utc");
            return record;
        }

        #endregion
    }
}
