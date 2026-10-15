namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral Permission data access. Thread-safe.
    /// </summary>
    public class AdoPermissionMethods : IPermissionMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _ResourceTypesTable;
        private readonly string _OperationTypesTable;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoPermissionMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("permissions");
            _ResourceTypesTable = driver.Table("permission_resource_types");
            _OperationTypesTable = driver.Table("permission_operation_types");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Permission> CreateAsync(Permission obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertRowAsync(connection, transaction, obj, ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<Permission?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Permission? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(obj, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Permission?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Permission? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id AND (tenant_id = @tenant_id OR tenant_id IS NULL);",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", id);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                },
                Map,
                token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(obj, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Permission?> ReadByNameAsync(string? tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            string sql = String.IsNullOrEmpty(tenantId)
                ? "SELECT * FROM " + _Table + " WHERE name = @name AND tenant_id IS NULL;"
                : "SELECT * FROM " + _Table + " WHERE name = @name AND (tenant_id = @tenant_id OR tenant_id IS NULL) ORDER BY CASE WHEN tenant_id IS NULL THEN 1 ELSE 0 END;";
            Permission? obj = await _Driver.QuerySingleAsync(
                sql,
                command =>
                {
                    AdoDatabaseDriver.Add(command, "name", name);
                    if (!String.IsNullOrEmpty(tenantId)) AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                },
                Map,
                token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(obj, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Permission> UpdateAsync(Permission obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, name = @name, permission_type = @permission_type, is_builtin = @is_builtin, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "name", obj.Name);
                    AdoDatabaseDriver.Add(command, "permission_type", obj.PermissionType.ToString());
                    AdoDatabaseDriver.Add(command, "is_builtin", obj.IsBuiltIn);
                    AdoDatabaseDriver.Add(command, "active", obj.Active);
                    AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                    AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
                }, ct).ConfigureAwait(false);

                await DeleteChildrenAsync(connection, transaction, obj.Id, ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
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
                await DeleteChildrenAsync(connection, transaction, id, ct).ConfigureAwait(false);
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
        public async Task<EnumerationResult<Permission>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(tenantId))
            {
                clauses.Add(query.IncludeBuiltIn ? "(tenant_id = @tenant_id OR tenant_id IS NULL)" : "tenant_id = @tenant_id");
            }
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("name", "search"));
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
                if (!String.IsNullOrEmpty(tenantId)) AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, true) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<Permission> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            foreach (Permission obj in objects) await LoadChildrenAsync(obj, token).ConfigureAwait(false);

            return EnumerationResult<Permission>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a Permission (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static Permission Map(DbDataReader reader)
        {
            Permission obj = new Permission();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.NullableStr(reader, "tenant_id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.PermissionType = AdoConverters.Enum<PermissionTypeEnum>(reader, "permission_type", PermissionTypeEnum.Permit);
            obj.IsBuiltIn = AdoConverters.Bool(reader, "is_builtin");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <summary>
        /// Load child rows into a Permission.
        /// </summary>
        /// <param name="obj">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task LoadChildrenAsync(Permission obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            obj.ResourceTypes = await _Driver.QueryAsync(
                "SELECT * FROM " + _ResourceTypesTable + " WHERE permission_id = @parentid ORDER BY id ASC;",
                command => AdoDatabaseDriver.Add(command, "parentid", obj.Id),
                reader => AdoConverters.Str(reader, "resource_type"),
                token).ConfigureAwait(false);

            obj.OperationTypes = await _Driver.QueryAsync(
                "SELECT * FROM " + _OperationTypesTable + " WHERE permission_id = @parentid ORDER BY id ASC;",
                command => AdoDatabaseDriver.Add(command, "parentid", obj.Id),
                reader => AdoConverters.Str(reader, "operation_type"),
                token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, Permission obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, name, permission_type, is_builtin, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @name, @permission_type, @is_builtin, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "name", obj.Name);
                AdoDatabaseDriver.Add(command, "permission_type", obj.PermissionType.ToString());
                AdoDatabaseDriver.Add(command, "is_builtin", obj.IsBuiltIn);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        private async Task InsertChildrenAsync(DbConnection connection, DbTransaction transaction, Permission obj, CancellationToken token)
        {
            string batch = IdGenerator.NewChildRowId() + "_";
            for (int i = 0; i < obj.ResourceTypes.Count; i++)
            {
                int position = i;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _ResourceTypesTable + " (id, permission_id, tenant_id, resource_type) VALUES (@id, @permission_id, @tenant_id, @resource_type);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", batch + position.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "res");
                        AdoDatabaseDriver.Add(command, "permission_id", obj.Id);
                        AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                        AdoDatabaseDriver.Add(command, "resource_type", obj.ResourceTypes[position]);
                    }, token).ConfigureAwait(false);
            }

            for (int i = 0; i < obj.OperationTypes.Count; i++)
            {
                int position = i;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _OperationTypesTable + " (id, permission_id, tenant_id, operation_type) VALUES (@id, @permission_id, @tenant_id, @operation_type);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", batch + position.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "ope");
                        AdoDatabaseDriver.Add(command, "permission_id", obj.Id);
                        AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                        AdoDatabaseDriver.Add(command, "operation_type", obj.OperationTypes[position]);
                    }, token).ConfigureAwait(false);
            }
        }

        private async Task DeleteChildrenAsync(DbConnection connection, DbTransaction transaction, string parentId, CancellationToken token)
        {
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _ResourceTypesTable + " WHERE permission_id = @parentid;", command => AdoDatabaseDriver.Add(command, "parentid", parentId), token).ConfigureAwait(false);
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _OperationTypesTable + " WHERE permission_id = @parentid;", command => AdoDatabaseDriver.Add(command, "parentid", parentId), token).ConfigureAwait(false);
        }

        #endregion
    }
}
