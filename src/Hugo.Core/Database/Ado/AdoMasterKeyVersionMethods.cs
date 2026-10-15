namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral master key version data access. Thread-safe.
    /// </summary>
    public class AdoMasterKeyVersionMethods : IMasterKeyVersionMethods
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
        public AdoMasterKeyVersionMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("master_key_versions");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<MasterKeyVersion>> ReadAllAsync(CancellationToken token = default)
        {
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " ORDER BY key_version ASC;", null, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MasterKeyVersion?> ReadAsync(int keyVersion, CancellationToken token = default)
        {
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE key_version = @v;", command => AdoDatabaseDriver.Add(command, "v", keyVersion), Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MasterKeyVersion> UpsertAsync(MasterKeyVersion obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                int updated = await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "UPDATE " + _Table + " SET canary = @canary, is_current = @is_current, last_update_utc = @last_update_utc WHERE key_version = @key_version;",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "canary", obj.Canary);
                        AdoDatabaseDriver.Add(command, "is_current", obj.IsCurrent);
                        AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
                        AdoDatabaseDriver.Add(command, "key_version", obj.KeyVersion);
                    }, ct).ConfigureAwait(false);
                if (updated > 0) return;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _Table + " (id, key_version, canary, is_current, created_utc, last_update_utc) VALUES (@id, @key_version, @canary, @is_current, @created_utc, @last_update_utc);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", obj.Id);
                        AdoDatabaseDriver.Add(command, "key_version", obj.KeyVersion);
                        AdoDatabaseDriver.Add(command, "canary", obj.Canary);
                        AdoDatabaseDriver.Add(command, "is_current", obj.IsCurrent);
                        AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                        AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
                    }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task SetCurrentAsync(int keyVersion, CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "UPDATE " + _Table + " SET is_current = @no, last_update_utc = @now WHERE key_version <> @v;",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "no", false);
                        AdoDatabaseDriver.Add(command, "now", now);
                        AdoDatabaseDriver.Add(command, "v", keyVersion);
                    }, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "UPDATE " + _Table + " SET is_current = @yes, last_update_utc = @now WHERE key_version = @v;",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "yes", true);
                        AdoDatabaseDriver.Add(command, "now", now);
                        AdoDatabaseDriver.Add(command, "v", keyVersion);
                    }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static MasterKeyVersion Map(DbDataReader reader)
        {
            MasterKeyVersion obj = new MasterKeyVersion();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.KeyVersion = AdoConverters.Int(reader, "key_version");
            obj.Canary = AdoConverters.Str(reader, "canary");
            obj.IsCurrent = AdoConverters.Bool(reader, "is_current");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion
    }
}
