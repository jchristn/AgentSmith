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
    /// Provider-neutral User data access. Thread-safe.
    /// </summary>
    public class AdoUserMethods : IUserMethods
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
        public AdoUserMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("users");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<User> CreateAsync(User obj, CancellationToken token = default)
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
        public async Task<User?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            User? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            User? obj = await _Driver.QuerySingleAsync(
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
        public async Task<User> UpdateAsync(User obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, first_name = @first_name, last_name = @last_name, email = @email, password_hash = @password_hash, is_admin = @is_admin, is_tenant_admin = @is_tenant_admin, must_change_password = @must_change_password, default_agent_profile_id = @default_agent_profile_id, preferred_locale = @preferred_locale, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "first_name", obj.FirstName);
                    AdoDatabaseDriver.Add(command, "last_name", obj.LastName);
                    AdoDatabaseDriver.Add(command, "email", obj.Email);
                    AdoDatabaseDriver.Add(command, "password_hash", obj.PasswordHash);
                    AdoDatabaseDriver.Add(command, "is_admin", obj.IsAdmin);
                    AdoDatabaseDriver.Add(command, "is_tenant_admin", obj.IsTenantAdmin);
                    AdoDatabaseDriver.Add(command, "must_change_password", obj.MustChangePassword);
                    AdoDatabaseDriver.Add(command, "default_agent_profile_id", obj.DefaultAgentProfileId);
                    AdoDatabaseDriver.Add(command, "preferred_locale", obj.PreferredLocale);
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
        public async Task<EnumerationResult<User>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
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
            List<User> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<User>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a User (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static User Map(DbDataReader reader)
        {
            User obj = new User();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.FirstName = AdoConverters.NullableStr(reader, "first_name");
            obj.LastName = AdoConverters.NullableStr(reader, "last_name");
            obj.Email = AdoConverters.Str(reader, "email");
            obj.PasswordHash = AdoConverters.Str(reader, "password_hash");
            obj.IsAdmin = AdoConverters.Bool(reader, "is_admin");
            obj.IsTenantAdmin = AdoConverters.Bool(reader, "is_tenant_admin");
            obj.MustChangePassword = AdoConverters.Bool(reader, "must_change_password");
            obj.DefaultAgentProfileId = AdoConverters.NullableStr(reader, "default_agent_profile_id");
            obj.PreferredLocale = AdoConverters.NullableStr(reader, "preferred_locale");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            return await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND LOWER(email) = LOWER(@email);",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "email", email);
                },
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteCascadeAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string credentials = _Driver.Table("credentials");
            int total = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                total = 0;
                Action<System.Data.Common.DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "user_id", userId);
                };
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("credential_scope_assignments") + " WHERE tenant_id = @tenant_id AND credential_id IN (SELECT id FROM " + credentials + " WHERE tenant_id = @tenant_id AND owner_user_id = @user_id);", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("auth_sessions") + " WHERE user_id = @user_id OR credential_id IN (SELECT id FROM " + credentials + " WHERE tenant_id = @tenant_id AND owner_user_id = @user_id);", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + credentials + " WHERE tenant_id = @tenant_id AND owner_user_id = @user_id;", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("user_role_assignments") + " WHERE tenant_id = @tenant_id AND user_id = @user_id;", bind, ct).ConfigureAwait(false);
                total += await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @user_id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return total;
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, User obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, first_name, last_name, email, password_hash, is_admin, is_tenant_admin, must_change_password, default_agent_profile_id, preferred_locale, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @first_name, @last_name, @email, @password_hash, @is_admin, @is_tenant_admin, @must_change_password, @default_agent_profile_id, @preferred_locale, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "first_name", obj.FirstName);
                AdoDatabaseDriver.Add(command, "last_name", obj.LastName);
                AdoDatabaseDriver.Add(command, "email", obj.Email);
                AdoDatabaseDriver.Add(command, "password_hash", obj.PasswordHash);
                AdoDatabaseDriver.Add(command, "is_admin", obj.IsAdmin);
                AdoDatabaseDriver.Add(command, "is_tenant_admin", obj.IsTenantAdmin);
                AdoDatabaseDriver.Add(command, "must_change_password", obj.MustChangePassword);
                AdoDatabaseDriver.Add(command, "default_agent_profile_id", obj.DefaultAgentProfileId);
                AdoDatabaseDriver.Add(command, "preferred_locale", obj.PreferredLocale);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
