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
    using Hugo.Core.Requests;

    /// <summary>
    /// Provider-neutral AuthSession data access. Thread-safe.
    /// </summary>
    public class AdoAuthSessionMethods : IAuthSessionMethods
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
        public AdoAuthSessionMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("auth_sessions");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AuthSession> CreateAsync(AuthSession obj, CancellationToken token = default)
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
        public async Task<AuthSession?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            AuthSession? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<AuthSession?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            AuthSession? obj = await _Driver.QuerySingleAsync(
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
        public async Task<AuthSession> UpdateAsync(AuthSession obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, user_id = @user_id, credential_id = @credential_id, principal_type = @principal_type, auth_scheme = @auth_scheme, token_id = @token_id, source_ip = @source_ip, user_agent = @user_agent, expires_utc = @expires_utc, last_used_utc = @last_used_utc, revoked_utc = @revoked_utc, revocation_reason = @revocation_reason, reauthenticated_utc = @reauthenticated_utc, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "user_id", obj.UserId);
                    AdoDatabaseDriver.Add(command, "credential_id", obj.CredentialId);
                    AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType.ToString());
                    AdoDatabaseDriver.Add(command, "auth_scheme", obj.AuthScheme.ToString());
                    AdoDatabaseDriver.Add(command, "token_id", obj.TokenId);
                    AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                    AdoDatabaseDriver.Add(command, "user_agent", obj.UserAgent);
                    AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
                    AdoDatabaseDriver.Add(command, "last_used_utc", obj.LastUsedUtc);
                    AdoDatabaseDriver.Add(command, "revoked_utc", obj.RevokedUtc);
                    AdoDatabaseDriver.Add(command, "revocation_reason", obj.RevocationReason);
                    AdoDatabaseDriver.Add(command, "reauthenticated_utc", obj.ReauthenticatedUtc);
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
        public async Task<EnumerationResult<AuthSession>> EnumerateAsync(string? tenantId, EnumerationQuery query, SessionFilter? filter = null, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            SessionFilter f = filter ?? new SessionFilter();

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(tenantId))
            {
                clauses.Add("tenant_id = @tenant_id");
            }
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            if (f.PrincipalType.HasValue) clauses.Add("principal_type = @principal_type");
            if (!String.IsNullOrEmpty(f.UserId)) clauses.Add("user_id = @user_id");
            if (!String.IsNullOrEmpty(f.CredentialId)) clauses.Add("credential_id = @credential_id");
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
                if (!String.IsNullOrEmpty(tenantId)) AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (f.PrincipalType.HasValue) AdoDatabaseDriver.Add(command, "principal_type", f.PrincipalType.Value.ToString());
                if (!String.IsNullOrEmpty(f.UserId)) AdoDatabaseDriver.Add(command, "user_id", f.UserId);
                if (!String.IsNullOrEmpty(f.CredentialId)) AdoDatabaseDriver.Add(command, "credential_id", f.CredentialId);
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<AuthSession> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<AuthSession>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a AuthSession (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static AuthSession Map(DbDataReader reader)
        {
            AuthSession obj = new AuthSession();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.NullableStr(reader, "tenant_id");
            obj.UserId = AdoConverters.NullableStr(reader, "user_id");
            obj.CredentialId = AdoConverters.NullableStr(reader, "credential_id");
            obj.PrincipalType = AdoConverters.Enum<PrincipalTypeEnum>(reader, "principal_type", PrincipalTypeEnum.User);
            obj.AuthScheme = AdoConverters.Enum<AuthSchemeEnum>(reader, "auth_scheme", AuthSchemeEnum.BearerToken);
            obj.TokenId = AdoConverters.Str(reader, "token_id");
            obj.SourceIp = AdoConverters.NullableStr(reader, "source_ip");
            obj.UserAgent = AdoConverters.NullableStr(reader, "user_agent");
            obj.ExpiresUtc = AdoConverters.Date(reader, "expires_utc");
            obj.LastUsedUtc = AdoConverters.NullableDate(reader, "last_used_utc");
            obj.RevokedUtc = AdoConverters.NullableDate(reader, "revoked_utc");
            obj.RevocationReason = AdoConverters.NullableStr(reader, "revocation_reason");
            obj.ReauthenticatedUtc = AdoConverters.NullableDate(reader, "reauthenticated_utc");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<AuthSession?> ReadByTokenIdAsync(string tokenId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tokenId)) throw new ArgumentNullException(nameof(tokenId));

            return await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE token_id = @token_id;",
                command => AdoDatabaseDriver.Add(command, "token_id", tokenId),
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> RevokeAsync(string id, string reason, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            int affected = await _Driver.NonQueryAsync(
                "UPDATE " + _Table + " SET revoked_utc = @now, revocation_reason = @reason, active = @inactive, last_update_utc = @now WHERE id = @id AND revoked_utc IS NULL;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", id);
                    AdoDatabaseDriver.Add(command, "reason", reason);
                    AdoDatabaseDriver.Add(command, "inactive", false);
                    AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                },
                token).ConfigureAwait(false);
            return affected > 0;
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
        public async Task<int> PruneExpiredAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            return await _Driver.NonQueryAsync(
                "DELETE FROM " + _Table + " WHERE expires_utc < @cutoff;",
                command => AdoDatabaseDriver.Add(command, "cutoff", olderThanUtc),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return await _Driver.NonQueryAsync("DELETE FROM " + _Table + " WHERE user_id = @id;", command => AdoDatabaseDriver.Add(command, "id", userId), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByCredentialAsync(string credentialId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(credentialId)) throw new ArgumentNullException(nameof(credentialId));
            return await _Driver.NonQueryAsync("DELETE FROM " + _Table + " WHERE credential_id = @id;", command => AdoDatabaseDriver.Add(command, "id", credentialId), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, AuthSession obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, user_id, credential_id, principal_type, auth_scheme, token_id, source_ip, user_agent, expires_utc, last_used_utc, revoked_utc, revocation_reason, reauthenticated_utc, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @user_id, @credential_id, @principal_type, @auth_scheme, @token_id, @source_ip, @user_agent, @expires_utc, @last_used_utc, @revoked_utc, @revocation_reason, @reauthenticated_utc, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "user_id", obj.UserId);
                AdoDatabaseDriver.Add(command, "credential_id", obj.CredentialId);
                AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType.ToString());
                AdoDatabaseDriver.Add(command, "auth_scheme", obj.AuthScheme.ToString());
                AdoDatabaseDriver.Add(command, "token_id", obj.TokenId);
                AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                AdoDatabaseDriver.Add(command, "user_agent", obj.UserAgent);
                AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
                AdoDatabaseDriver.Add(command, "last_used_utc", obj.LastUsedUtc);
                AdoDatabaseDriver.Add(command, "revoked_utc", obj.RevokedUtc);
                AdoDatabaseDriver.Add(command, "revocation_reason", obj.RevocationReason);
                AdoDatabaseDriver.Add(command, "reauthenticated_utc", obj.ReauthenticatedUtc);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
