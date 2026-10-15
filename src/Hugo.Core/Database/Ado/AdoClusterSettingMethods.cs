namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral ClusterSetting data access. Thread-safe.
    /// </summary>
    public class AdoClusterSettingMethods : IClusterSettingMethods
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
        public AdoClusterSettingMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("cluster_settings");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ClusterSetting> CreateAsync(ClusterSetting obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertRowAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<ClusterSetting?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            ClusterSetting? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<ClusterSetting> UpdateAsync(ClusterSetting obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET setting_key = @setting_key, setting_value = @setting_value, updated_by = @updated_by, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "setting_key", obj.SettingKey);
                    AdoDatabaseDriver.Add(command, "setting_value", obj.SettingValue);
                    AdoDatabaseDriver.Add(command, "updated_by", obj.UpdatedBy);
                    AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int affected = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE id = @id;", command => AdoDatabaseDriver.Add(command, "id", id), ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            object? result = await _Driver.ScalarAsync("SELECT 1 FROM " + _Table + " WHERE id = @id;", command => AdoDatabaseDriver.Add(command, "id", id), token).ConfigureAwait(false);
            return result != null;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ClusterSetting>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<ClusterSetting> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<ClusterSetting>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a ClusterSetting (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static ClusterSetting Map(DbDataReader reader)
        {
            ClusterSetting obj = new ClusterSetting();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.SettingKey = AdoConverters.Str(reader, "setting_key");
            obj.SettingValue = AdoConverters.NullableStr(reader, "setting_value");
            obj.UpdatedBy = AdoConverters.NullableStr(reader, "updated_by");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<ClusterSetting?> ReadByKeyAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE setting_key = @key;", command => AdoDatabaseDriver.Add(command, "key", key), Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ClusterSetting> UpsertAsync(string key, string? value, string? updatedBy, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            ClusterSetting? existing = await ReadByKeyAsync(key, token).ConfigureAwait(false);
            if (existing == null)
            {
                ClusterSetting created = new ClusterSetting();
                created.SettingKey = key;
                created.SettingValue = value;
                created.UpdatedBy = updatedBy;
                created.CreatedUtc = DateTime.UtcNow;
                return await CreateAsync(created, token).ConfigureAwait(false);
            }
            existing.SettingValue = value;
            existing.UpdatedBy = updatedBy;
            return await UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ClusterSetting>> ListAllAsync(CancellationToken token = default)
        {
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " ORDER BY setting_key ASC;", null, Map, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, ClusterSetting obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, setting_key, setting_value, updated_by, created_utc, last_update_utc) VALUES (@id, @setting_key, @setting_value, @updated_by, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "setting_key", obj.SettingKey);
                AdoDatabaseDriver.Add(command, "setting_value", obj.SettingValue);
                AdoDatabaseDriver.Add(command, "updated_by", obj.UpdatedBy);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
