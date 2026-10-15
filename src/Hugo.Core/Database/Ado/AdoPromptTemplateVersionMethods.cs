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
    /// Provider-neutral PromptTemplateVersion data access. Thread-safe.
    /// </summary>
    public class AdoPromptTemplateVersionMethods : IPromptTemplateVersionMethods
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
        public AdoPromptTemplateVersionMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("prompt_template_versions");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<PromptTemplateVersion> CreateAsync(PromptTemplateVersion obj, CancellationToken token = default)
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
        public async Task<PromptTemplateVersion?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            PromptTemplateVersion? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<PromptTemplateVersion?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            PromptTemplateVersion? obj = await _Driver.QuerySingleAsync(
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
        public async Task<PromptTemplateVersion> UpdateAsync(PromptTemplateVersion obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, template_id = @template_id, version = @version, content = @content, change_note = @change_note, created_by = @created_by, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "template_id", obj.TemplateId);
                    AdoDatabaseDriver.Add(command, "version", obj.Version);
                    AdoDatabaseDriver.Add(command, "content", obj.Content);
                    AdoDatabaseDriver.Add(command, "change_note", obj.ChangeNote);
                    AdoDatabaseDriver.Add(command, "created_by", obj.CreatedBy);
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
        public async Task<EnumerationResult<PromptTemplateVersion>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
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
            List<PromptTemplateVersion> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<PromptTemplateVersion>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a PromptTemplateVersion (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static PromptTemplateVersion Map(DbDataReader reader)
        {
            PromptTemplateVersion obj = new PromptTemplateVersion();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.NullableStr(reader, "tenant_id");
            obj.TemplateId = AdoConverters.Str(reader, "template_id");
            obj.Version = AdoConverters.Int(reader, "version");
            obj.Content = AdoConverters.Str(reader, "content");
            obj.ChangeNote = AdoConverters.NullableStr(reader, "change_note");
            obj.CreatedBy = AdoConverters.NullableStr(reader, "created_by");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<List<PromptTemplateVersion>> ListByTemplateAsync(string templateId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(templateId)) throw new ArgumentNullException(nameof(templateId));

            return await _Driver.QueryAsync(
                "SELECT * FROM " + _Table + " WHERE template_id = @template_id ORDER BY version DESC;",
                command => AdoDatabaseDriver.Add(command, "template_id", templateId),
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<PromptTemplateVersion?> ReadByVersionAsync(string templateId, int version, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(templateId)) throw new ArgumentNullException(nameof(templateId));

            return await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE template_id = @template_id AND version = @version;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "template_id", templateId);
                    AdoDatabaseDriver.Add(command, "version", version);
                },
                Map,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTemplateAsync(string templateId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(templateId)) throw new ArgumentNullException(nameof(templateId));

            return await _Driver.NonQueryAsync(
                "DELETE FROM " + _Table + " WHERE template_id = @template_id;",
                command => AdoDatabaseDriver.Add(command, "template_id", templateId),
                token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, PromptTemplateVersion obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, template_id, version, content, change_note, created_by, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @template_id, @version, @content, @change_note, @created_by, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "template_id", obj.TemplateId);
                AdoDatabaseDriver.Add(command, "version", obj.Version);
                AdoDatabaseDriver.Add(command, "content", obj.Content);
                AdoDatabaseDriver.Add(command, "change_note", obj.ChangeNote);
                AdoDatabaseDriver.Add(command, "created_by", obj.CreatedBy);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
