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
    /// Provider-neutral Credential data access. Thread-safe.
    /// </summary>
    public class AdoCredentialMethods : ICredentialMethods
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
        public AdoCredentialMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("credentials");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Credential> CreateAsync(Credential obj, CancellationToken token = default)
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
        public async Task<Credential?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Credential? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Credential? obj = await _Driver.QuerySingleAsync(
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
        public async Task<Credential> UpdateAsync(Credential obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, owner_user_id = @owner_user_id, name = @name, access_key = @access_key, secret_key_encrypted = @secret_key_encrypted, secret_key_last4 = @secret_key_last4, auth_mode = @auth_mode, agent_profile_id = @agent_profile_id, lock_to_profile = @lock_to_profile, inherit_owner_bypass = @inherit_owner_bypass, last_used_utc = @last_used_utc, expires_utc = @expires_utc, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "owner_user_id", obj.UserId);
                    AdoDatabaseDriver.Add(command, "name", obj.Name);
                    AdoDatabaseDriver.Add(command, "access_key", obj.AccessKey);
                    AdoDatabaseDriver.Add(command, "secret_key_encrypted", obj.SecretKeyEncrypted);
                    AdoDatabaseDriver.Add(command, "secret_key_last4", obj.SecretKeyLast4);
                    AdoDatabaseDriver.Add(command, "auth_mode", obj.AuthMode.ToString());
                    AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
                    AdoDatabaseDriver.Add(command, "lock_to_profile", obj.LockToProfile);
                    AdoDatabaseDriver.Add(command, "inherit_owner_bypass", obj.InheritOwnerBypass);
                    AdoDatabaseDriver.Add(command, "last_used_utc", obj.LastUsedUtc);
                    AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
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
        public async Task<EnumerationResult<Credential>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
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
            List<Credential> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<Credential>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a Credential (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static Credential Map(DbDataReader reader)
        {
            Credential obj = new Credential();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.UserId = AdoConverters.Str(reader, "owner_user_id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.AccessKey = AdoConverters.Str(reader, "access_key");
            obj.SecretKeyEncrypted = AdoConverters.Str(reader, "secret_key_encrypted");
            obj.SecretKeyLast4 = AdoConverters.NullableStr(reader, "secret_key_last4");
            obj.AuthMode = AdoConverters.Enum<CredentialAuthModeEnum>(reader, "auth_mode", CredentialAuthModeEnum.Hybrid);
            obj.AgentProfileId = AdoConverters.NullableStr(reader, "agent_profile_id");
            obj.LockToProfile = AdoConverters.Bool(reader, "lock_to_profile");
            obj.InheritOwnerBypass = AdoConverters.Bool(reader, "inherit_owner_bypass");
            obj.LastUsedUtc = AdoConverters.NullableDate(reader, "last_used_utc");
            obj.ExpiresUtc = AdoConverters.NullableDate(reader, "expires_utc");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadByAccessKeyAsync(string accessKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));

            return await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE access_key = @access_key;",
                command => AdoDatabaseDriver.Add(command, "access_key", accessKey),
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task TouchLastUsedAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await _Driver.NonQueryAsync(
                "UPDATE " + _Table + " SET last_used_utc = @now WHERE id = @id;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", id);
                    AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                },
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Credential>> ListByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));

            return await _Driver.QueryAsync(
                "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND owner_user_id = @owner_user_id ORDER BY created_utc ASC;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "owner_user_id", userId);
                },
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteCascadeAsync(string tenantId, string credentialId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(credentialId)) throw new ArgumentNullException(nameof(credentialId));
            int total = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                total = 0;
                Action<System.Data.Common.DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "credential_id", credentialId);
                };
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("credential_scope_assignments") + " WHERE tenant_id = @tenant_id AND credential_id = @credential_id;", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("auth_sessions") + " WHERE credential_id = @credential_id;", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @credential_id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return total;
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, Credential obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, owner_user_id, name, access_key, secret_key_encrypted, secret_key_last4, auth_mode, agent_profile_id, lock_to_profile, inherit_owner_bypass, last_used_utc, expires_utc, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @owner_user_id, @name, @access_key, @secret_key_encrypted, @secret_key_last4, @auth_mode, @agent_profile_id, @lock_to_profile, @inherit_owner_bypass, @last_used_utc, @expires_utc, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "owner_user_id", obj.UserId);
                AdoDatabaseDriver.Add(command, "name", obj.Name);
                AdoDatabaseDriver.Add(command, "access_key", obj.AccessKey);
                AdoDatabaseDriver.Add(command, "secret_key_encrypted", obj.SecretKeyEncrypted);
                AdoDatabaseDriver.Add(command, "secret_key_last4", obj.SecretKeyLast4);
                AdoDatabaseDriver.Add(command, "auth_mode", obj.AuthMode.ToString());
                AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
                AdoDatabaseDriver.Add(command, "lock_to_profile", obj.LockToProfile);
                AdoDatabaseDriver.Add(command, "inherit_owner_bypass", obj.InheritOwnerBypass);
                AdoDatabaseDriver.Add(command, "last_used_utc", obj.LastUsedUtc);
                AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
