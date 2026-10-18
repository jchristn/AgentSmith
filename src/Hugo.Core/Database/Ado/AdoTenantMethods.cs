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
    /// Provider-neutral Tenant data access. Thread-safe.
    /// </summary>
    public class AdoTenantMethods : ITenantMethods
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
        public AdoTenantMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("tenants");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Tenant> CreateAsync(Tenant obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertRowAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<Tenant?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Tenant? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Tenant?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            Tenant? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE name = @name;",
                command => AdoDatabaseDriver.Add(command, "name", name),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Tenant> UpdateAsync(Tenant obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET name = @name, description = @description, active = @active, is_protected = @is_protected, parent_tenant_id = @parent_tenant_id, region = @region, default_agent_profile_id = @default_agent_profile_id, lockdown = @lockdown, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "name", obj.Name);
                    AdoDatabaseDriver.Add(command, "description", obj.Description);
                    AdoDatabaseDriver.Add(command, "active", obj.Active);
                    AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                    AdoDatabaseDriver.Add(command, "parent_tenant_id", obj.ParentTenantId);
                    AdoDatabaseDriver.Add(command, "region", obj.Region);
                    AdoDatabaseDriver.Add(command, "default_agent_profile_id", obj.DefaultAgentProfileId);
                    AdoDatabaseDriver.Add(command, "lockdown", obj.Lockdown);
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
        public async Task<EnumerationResult<Tenant>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("name", "search"));
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, true) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<Tenant> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<Tenant>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a Tenant (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static Tenant Map(DbDataReader reader)
        {
            Tenant obj = new Tenant();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.Description = AdoConverters.NullableStr(reader, "description");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.ParentTenantId = AdoConverters.NullableStr(reader, "parent_tenant_id");
            obj.Region = AdoConverters.NullableStr(reader, "region");
            obj.DefaultAgentProfileId = AdoConverters.NullableStr(reader, "default_agent_profile_id");
            obj.Lockdown = AdoConverters.Bool(reader, "lockdown");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<int> DeleteCascadeAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            HashSet<string> withoutTenant = new HashSet<string>(Hugo.Core.Database.TableCatalog.GlobalTables, StringComparer.OrdinalIgnoreCase);

            int total = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                total = 0;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET last_update_utc=last_update_utc WHERE id=@tenant_id;", command => AdoDatabaseDriver.Add(command, "tenant_id", tenantId), ct).ConfigureAwait(false);
                foreach (string table in Hugo.Core.Database.TableCatalog.AllTables)
                {
                    if (withoutTenant.Contains(table)) continue;
                    total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table(table) + " WHERE tenant_id = @tenant_id;", command => AdoDatabaseDriver.Add(command, "tenant_id", tenantId), ct).ConfigureAwait(false);
                }
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE id = @id;", command => AdoDatabaseDriver.Add(command, "id", tenantId), ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return total;
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, Tenant obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, name, description, active, is_protected, parent_tenant_id, region, default_agent_profile_id, lockdown, created_utc, last_update_utc) VALUES (@id, @name, @description, @active, @is_protected, @parent_tenant_id, @region, @default_agent_profile_id, @lockdown, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "name", obj.Name);
                AdoDatabaseDriver.Add(command, "description", obj.Description);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "parent_tenant_id", obj.ParentTenantId);
                    AdoDatabaseDriver.Add(command, "region", obj.Region);
                    AdoDatabaseDriver.Add(command, "default_agent_profile_id", obj.DefaultAgentProfileId);
                    AdoDatabaseDriver.Add(command, "lockdown", obj.Lockdown);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
