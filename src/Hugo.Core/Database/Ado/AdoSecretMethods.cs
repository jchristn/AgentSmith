namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral secret data access over the secrets and secret_values tables. Thread-safe.
    /// </summary>
    public class AdoSecretMethods : ISecretMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _ValuesTable;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoSecretMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("secrets");
            _ValuesTable = driver.Table("secret_values");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Secret> CreateAsync(Secret obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertRowAsync(connection, transaction, obj, ct).ConfigureAwait(false);
                await InsertValuesAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<Secret?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Secret? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            if (obj != null) obj.Values = await ReadValuesAsync(obj.Id, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Secret?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Secret? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id AND tenant_id = @tenant_id;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", id);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                },
                Map,
                token).ConfigureAwait(false);
            if (obj != null) obj.Values = await ReadValuesAsync(obj.Id, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Secret?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            Secret? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE name = @name AND tenant_id = @tenant_id;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "name", name);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                },
                Map,
                token).ConfigureAwait(false);
            if (obj != null) obj.Values = await ReadValuesAsync(obj.Id, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Secret> UpdateAsync(Secret obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET owner_user_id = @owner_user_id, name = @name, secret_kind = @secret_kind, description = @description, key_version = @key_version, fingerprint = @fingerprint, last_rotated_utc = @last_rotated_utc, last_revealed_utc = @last_revealed_utc, expires_utc = @expires_utc, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id AND tenant_id = @tenant_id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command => Bind(command, obj, false), ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _ValuesTable + " WHERE secret_id = @secret_id;", command => AdoDatabaseDriver.Add(command, "secret_id", obj.Id), ct).ConfigureAwait(false);
                await InsertValuesAsync(connection, transaction, obj, ct).ConfigureAwait(false);
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
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _ValuesTable + " WHERE secret_id = @id;", command => AdoDatabaseDriver.Add(command, "id", id), ct).ConfigureAwait(false);
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
        public async Task<EnumerationResult<Secret>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(tenantId)) clauses.Add("tenant_id = @tenant_id");
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
            List<Secret> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            if (objects.Count > 0)
            {
                List<string> parameters = new List<string>();
                for (int i = 0; i < objects.Count; i++) parameters.Add("@s" + i);
                List<SecretFieldRow> rows = await _Driver.QueryAsync(
                    "SELECT secret_id, field_name FROM " + _ValuesTable + " WHERE secret_id IN (" + String.Join(", ", parameters) + ");",
                    command =>
                    {
                        for (int i = 0; i < objects.Count; i++) AdoDatabaseDriver.Add(command, "s" + i, objects[i].Id);
                    },
                    reader => new SecretFieldRow(AdoConverters.Str(reader, "secret_id"), AdoConverters.Str(reader, "field_name")),
                    token).ConfigureAwait(false);
                foreach (Secret secret in objects)
                {
                    secret.Fields = rows.Where(r => r.SecretId == secret.Id).Select(r => r.FieldName).OrderBy(n => n, StringComparer.Ordinal).ToList();
                }
            }

            return EnumerationResult<Secret>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<SecretValue>> ReadValuesNotAtVersionAsync(int keyVersion, int maxResults, CancellationToken token = default)
        {
            int max = Math.Clamp(maxResults, 1, 10000);
            return await _Driver.QueryAsync(
                "SELECT * FROM " + _ValuesTable + " WHERE key_version <> @version ORDER BY id ASC" + _Driver.Dialect.LimitOffsetClause() + ";",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "version", keyVersion);
                    AdoDatabaseDriver.Add(command, "max", max);
                    AdoDatabaseDriver.Add(command, "skip", 0);
                },
                MapValue,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<long> CountValuesNotAtVersionAsync(int keyVersion, CancellationToken token = default)
        {
            object? result = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _ValuesTable + " WHERE key_version <> @version;", command => AdoDatabaseDriver.Add(command, "version", keyVersion), token).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToInt64(result);
        }

        /// <inheritdoc />
        public async Task<long> CountValuesAsync(CancellationToken token = default)
        {
            object? result = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _ValuesTable + ";", null, token).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToInt64(result);
        }

        /// <inheritdoc />
        public async Task<int> UpdateCiphertextsAsync(List<SecretValue> values, CancellationToken token = default)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Count == 0) return 0;

            int updated = 0;
            DateTime now = DateTime.UtcNow;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                updated = 0;
                foreach (SecretValue value in values)
                {
                    updated += await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                        "UPDATE " + _ValuesTable + " SET ciphertext = @ciphertext, key_version = @key_version, last_update_utc = @now WHERE id = @id;",
                        command =>
                        {
                            AdoDatabaseDriver.Add(command, "id", value.Id);
                            AdoDatabaseDriver.Add(command, "ciphertext", value.Ciphertext);
                            AdoDatabaseDriver.Add(command, "key_version", value.KeyVersion);
                            AdoDatabaseDriver.Add(command, "now", now);
                        }, ct).ConfigureAwait(false);
                }
                foreach (string secretId in values.Select(v => v.SecretId).Distinct(StringComparer.Ordinal))
                {
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                        "UPDATE " + _Table + " SET key_version = (SELECT MIN(key_version) FROM " + _ValuesTable + " WHERE secret_id = @id), last_update_utc = @now WHERE id = @id;",
                        command =>
                        {
                            AdoDatabaseDriver.Add(command, "id", secretId);
                            AdoDatabaseDriver.Add(command, "now", now);
                        }, ct).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
            return updated;
        }

        /// <summary>
        /// Map a row to a secret (values not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static Secret Map(DbDataReader reader)
        {
            Secret obj = new Secret();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.OwnerUserId = AdoConverters.NullableStr(reader, "owner_user_id");
            obj.Name = AdoConverters.Str(reader, "name");
            obj.Kind = AdoConverters.Enum<SecretKindEnum>(reader, "secret_kind", SecretKindEnum.Generic);
            obj.Description = AdoConverters.NullableStr(reader, "description");
            obj.KeyVersion = AdoConverters.Int(reader, "key_version");
            obj.Fingerprint = AdoConverters.NullableStr(reader, "fingerprint");
            obj.LastRotatedUtc = AdoConverters.NullableDate(reader, "last_rotated_utc");
            obj.LastRevealedUtc = AdoConverters.NullableDate(reader, "last_revealed_utc");
            obj.ExpiresUtc = AdoConverters.NullableDate(reader, "expires_utc");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <summary>
        /// Map a row to a secret value.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static SecretValue MapValue(DbDataReader reader)
        {
            SecretValue obj = new SecretValue();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.SecretId = AdoConverters.Str(reader, "secret_id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.FieldName = AdoConverters.Str(reader, "field_name");
            obj.Ciphertext = AdoConverters.Str(reader, "ciphertext");
            obj.KeyVersion = AdoConverters.Int(reader, "key_version");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private async Task<List<SecretValue>> ReadValuesAsync(string secretId, CancellationToken token)
        {
            return await _Driver.QueryAsync(
                "SELECT * FROM " + _ValuesTable + " WHERE secret_id = @secret_id ORDER BY field_name ASC;",
                command => AdoDatabaseDriver.Add(command, "secret_id", secretId),
                MapValue,
                token).ConfigureAwait(false);
        }

        private static void Bind(DbCommand command, Secret obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "owner_user_id", obj.OwnerUserId);
            AdoDatabaseDriver.Add(command, "name", obj.Name);
            AdoDatabaseDriver.Add(command, "secret_kind", obj.Kind.ToString());
            AdoDatabaseDriver.Add(command, "description", obj.Description);
            AdoDatabaseDriver.Add(command, "key_version", obj.KeyVersion);
            AdoDatabaseDriver.Add(command, "fingerprint", obj.Fingerprint);
            AdoDatabaseDriver.Add(command, "last_rotated_utc", obj.LastRotatedUtc);
            AdoDatabaseDriver.Add(command, "last_revealed_utc", obj.LastRevealedUtc);
            AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
            AdoDatabaseDriver.Add(command, "active", obj.Active);
            AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, Secret obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, owner_user_id, name, secret_kind, description, key_version, fingerprint, last_rotated_utc, last_revealed_utc, expires_utc, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @owner_user_id, @name, @secret_kind, @description, @key_version, @fingerprint, @last_rotated_utc, @last_revealed_utc, @expires_utc, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command => Bind(command, obj, true), token).ConfigureAwait(false);
        }

        private async Task InsertValuesAsync(DbConnection connection, DbTransaction transaction, Secret obj, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            foreach (SecretValue value in obj.Values)
            {
                value.SecretId = obj.Id;
                value.TenantId = obj.TenantId;
                value.LastUpdateUtc = now;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _ValuesTable + " (id, secret_id, tenant_id, field_name, ciphertext, key_version, created_utc, last_update_utc) VALUES (@id, @secret_id, @tenant_id, @field_name, @ciphertext, @key_version, @created_utc, @last_update_utc);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", value.Id);
                        AdoDatabaseDriver.Add(command, "secret_id", value.SecretId);
                        AdoDatabaseDriver.Add(command, "tenant_id", value.TenantId);
                        AdoDatabaseDriver.Add(command, "field_name", value.FieldName);
                        AdoDatabaseDriver.Add(command, "ciphertext", value.Ciphertext);
                        AdoDatabaseDriver.Add(command, "key_version", value.KeyVersion);
                        AdoDatabaseDriver.Add(command, "created_utc", value.CreatedUtc);
                        AdoDatabaseDriver.Add(command, "last_update_utc", value.LastUpdateUtc);
                    }, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
