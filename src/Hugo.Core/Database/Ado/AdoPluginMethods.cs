namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Helpers;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral plugin record data access (global). Thread-safe.
    /// </summary>
    public class AdoPluginMethods : IPluginMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Types;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoPluginMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("plugins");
            _Types = driver.Table("plugin_connector_types");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<PluginRecord> UpsertAsync(PluginRecord obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                List<PluginRecord> existing = await AdoDatabaseDriver.QueryAsync(connection, transaction, "SELECT * FROM " + _Table + " WHERE name = @name;", command => AdoDatabaseDriver.Add(command, "name", obj.Name), Map, ct).ConfigureAwait(false);
                obj.LastUpdateUtc = DateTime.UtcNow;
                if (existing.Count > 0)
                {
                    obj.Id = existing[0].Id;
                    obj.CreatedUtc = existing[0].CreatedUtc;
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Types + " WHERE plugin_id = @id;", command => AdoDatabaseDriver.Add(command, "id", obj.Id), ct).ConfigureAwait(false);
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET version = @version, assembly_path = @assembly_path, sha256 = @sha256, enabled = @enabled, loaded = @loaded, load_error = @load_error, last_update_utc = @last_update_utc WHERE id = @id;", command => Bind(command, obj, false), ct).ConfigureAwait(false);
                }
                else
                {
                    obj.CreatedUtc = obj.LastUpdateUtc;
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Table + " (id, name, version, assembly_path, sha256, enabled, loaded, load_error, created_utc, last_update_utc) VALUES (@id, @name, @version, @assembly_path, @sha256, @enabled, @loaded, @load_error, @created_utc, @last_update_utc);", command => Bind(command, obj, true), ct).ConfigureAwait(false);
                }
                foreach (string type in obj.ConnectorTypes.Distinct(StringComparer.Ordinal))
                {
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Types + " (id, plugin_id, connector_type) VALUES (@id, @plugin_id, @connector_type);", command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", IdGenerator.NewChildRowId());
                        AdoDatabaseDriver.Add(command, "plugin_id", obj.Id);
                        AdoDatabaseDriver.Add(command, "connector_type", type);
                    }, ct).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<PluginRecord?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            List<PluginRecord> records = await LoadAsync(" WHERE name = @name", command => AdoDatabaseDriver.Add(command, "name", name), token).ConfigureAwait(false);
            return records.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<PluginRecord>> ListAsync(CancellationToken token = default)
        {
            return await LoadAsync(String.Empty, null, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<List<PluginRecord>> LoadAsync(string where, Action<DbCommand>? bind, CancellationToken token)
        {
            List<PluginRecord> records = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + " ORDER BY name ASC;", bind, Map, token).ConfigureAwait(false);
            foreach (PluginRecord record in records)
            {
                record.ConnectorTypes = await _Driver.QueryAsync("SELECT connector_type FROM " + _Types + " WHERE plugin_id = @id ORDER BY connector_type ASC;", command => AdoDatabaseDriver.Add(command, "id", record.Id), reader => AdoConverters.Str(reader, "connector_type"), token).ConfigureAwait(false);
            }
            return records;
        }

        private static PluginRecord Map(DbDataReader reader)
        {
            PluginRecord obj = new PluginRecord();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.Version = AdoConverters.NullableStr(reader, "version");
            obj.AssemblyPath = AdoConverters.Str(reader, "assembly_path");
            obj.Sha256 = AdoConverters.NullableStr(reader, "sha256");
            obj.Enabled = AdoConverters.Bool(reader, "enabled");
            obj.Loaded = AdoConverters.Bool(reader, "loaded");
            obj.LoadError = AdoConverters.NullableStr(reader, "load_error");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        private static void Bind(DbCommand command, PluginRecord obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            if (insert) AdoDatabaseDriver.Add(command, "name", obj.Name);
            AdoDatabaseDriver.Add(command, "version", obj.Version);
            AdoDatabaseDriver.Add(command, "assembly_path", obj.AssemblyPath);
            AdoDatabaseDriver.Add(command, "sha256", obj.Sha256);
            AdoDatabaseDriver.Add(command, "enabled", obj.Enabled);
            AdoDatabaseDriver.Add(command, "loaded", obj.Loaded);
            AdoDatabaseDriver.Add(command, "load_error", obj.LoadError);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
