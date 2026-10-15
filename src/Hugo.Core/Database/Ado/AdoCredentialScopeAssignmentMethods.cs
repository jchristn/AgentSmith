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
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral CredentialScopeAssignment data access. Thread-safe.
    /// </summary>
    public class AdoCredentialScopeAssignmentMethods : ICredentialScopeAssignmentMethods
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
        public AdoCredentialScopeAssignmentMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("credential_scope_assignments");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment> CreateAsync(CredentialScopeAssignment obj, CancellationToken token = default)
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
        public async Task<CredentialScopeAssignment?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            CredentialScopeAssignment? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            CredentialScopeAssignment? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id AND tenant_id = @tenant_id;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", id);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                },
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<CredentialScopeAssignment> UpdateAsync(CredentialScopeAssignment obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, credential_id = @credential_id, role_id = @role_id, role_name = @role_name, resource_scope = @resource_scope, resource_id = @resource_id, inherits_to_children = @inherits_to_children, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "credential_id", obj.CredentialId);
                    AdoDatabaseDriver.Add(command, "role_id", obj.RoleId);
                    AdoDatabaseDriver.Add(command, "role_name", obj.RoleName);
                    AdoDatabaseDriver.Add(command, "resource_scope", obj.ResourceScope.ToString());
                    AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
                    AdoDatabaseDriver.Add(command, "inherits_to_children", obj.InheritsToChildren);
                    AdoDatabaseDriver.Add(command, "active", obj.Active);
                    AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
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
        public async Task<EnumerationResult<CredentialScopeAssignment>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(tenantId))
            {
                clauses.Add("tenant_id = @tenant_id");
            }
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
                if (!String.IsNullOrEmpty(tenantId)) AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<CredentialScopeAssignment> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<CredentialScopeAssignment>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a CredentialScopeAssignment (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static CredentialScopeAssignment Map(DbDataReader reader)
        {
            CredentialScopeAssignment obj = new CredentialScopeAssignment();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.CredentialId = AdoConverters.Str(reader, "credential_id");
            obj.RoleId = AdoConverters.NullableStr(reader, "role_id");
            obj.RoleName = AdoConverters.NullableStr(reader, "role_name");
            obj.ResourceScope = AdoConverters.Enum<ResourceScopeEnum>(reader, "resource_scope", ResourceScopeEnum.Tenant);
            obj.ResourceId = AdoConverters.NullableStr(reader, "resource_id");
            obj.InheritsToChildren = AdoConverters.Bool(reader, "inherits_to_children");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<List<CredentialScopeAssignment>> ListByCredentialAsync(string tenantId, string credentialId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(credentialId)) throw new ArgumentNullException(nameof(credentialId));

            return await _Driver.QueryAsync(
                "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND credential_id = @credential_id AND active = " + _Driver.Dialect.BooleanLiteral(true) + ";",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "credential_id", credentialId);
                },
                Map,
                token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, CredentialScopeAssignment obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, credential_id, role_id, role_name, resource_scope, resource_id, inherits_to_children, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @credential_id, @role_id, @role_name, @resource_scope, @resource_id, @inherits_to_children, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "credential_id", obj.CredentialId);
                AdoDatabaseDriver.Add(command, "role_id", obj.RoleId);
                AdoDatabaseDriver.Add(command, "role_name", obj.RoleName);
                AdoDatabaseDriver.Add(command, "resource_scope", obj.ResourceScope.ToString());
                AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
                AdoDatabaseDriver.Add(command, "inherits_to_children", obj.InheritsToChildren);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
