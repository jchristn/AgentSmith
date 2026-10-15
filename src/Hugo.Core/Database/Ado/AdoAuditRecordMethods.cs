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
    using Hugo.Core.Responses;

    /// <summary>
    /// Provider-neutral AuditRecord data access. Thread-safe.
    /// </summary>
    public class AdoAuditRecordMethods : IAuditRecordMethods
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
        public AdoAuditRecordMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("audit_records");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AuditRecord> CreateAsync(AuditRecord obj, CancellationToken token = default)
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
        public async Task<AuditRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            AuditRecord? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<AuditRecord?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            AuditRecord? obj = await _Driver.QuerySingleAsync(
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
        public async Task<AuditRecord> UpdateAsync(AuditRecord obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, event_type = @event_type, request_id = @request_id, correlation_id = @correlation_id, trace_id = @trace_id, auth_scheme = @auth_scheme, principal_type = @principal_type, principal_id = @principal_id, principal_name = @principal_name, session_id = @session_id, resource_type = @resource_type, resource_id = @resource_id, operation = @operation, request_type = @request_type, method = @method, path = @path, source_ip = @source_ip, auth_result = @auth_result, authorization_result = @authorization_result, reason = @reason, bypass_reason = @bypass_reason, status_code = @status_code, description = @description, active = @active, is_protected = @is_protected, last_update_utc = @last_update_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "event_type", obj.EventType.ToString());
                    AdoDatabaseDriver.Add(command, "request_id", obj.RequestId);
                    AdoDatabaseDriver.Add(command, "correlation_id", obj.CorrelationId);
                    AdoDatabaseDriver.Add(command, "trace_id", obj.TraceId);
                    AdoDatabaseDriver.Add(command, "auth_scheme", obj.AuthScheme?.ToString());
                    AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType?.ToString());
                    AdoDatabaseDriver.Add(command, "principal_id", obj.PrincipalId);
                    AdoDatabaseDriver.Add(command, "principal_name", obj.PrincipalName);
                    AdoDatabaseDriver.Add(command, "session_id", obj.SessionId);
                    AdoDatabaseDriver.Add(command, "resource_type", obj.ResourceType);
                    AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
                    AdoDatabaseDriver.Add(command, "operation", obj.Operation);
                    AdoDatabaseDriver.Add(command, "request_type", obj.RequestType);
                    AdoDatabaseDriver.Add(command, "method", obj.Method);
                    AdoDatabaseDriver.Add(command, "path", obj.Path);
                    AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                    AdoDatabaseDriver.Add(command, "auth_result", obj.AuthResult?.ToString());
                    AdoDatabaseDriver.Add(command, "authorization_result", obj.AuthorizationResult?.ToString());
                    AdoDatabaseDriver.Add(command, "reason", obj.Reason);
                    AdoDatabaseDriver.Add(command, "bypass_reason", obj.BypassReason);
                    AdoDatabaseDriver.Add(command, "status_code", obj.StatusCode);
                    AdoDatabaseDriver.Add(command, "description", obj.Description);
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
        public async Task<EnumerationResult<AuditRecord>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
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
            List<AuditRecord> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            return EnumerationResult<AuditRecord>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a AuditRecord (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static AuditRecord Map(DbDataReader reader)
        {
            AuditRecord obj = new AuditRecord();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.NullableStr(reader, "tenant_id");
            obj.EventType = AdoConverters.Enum<AuditEventTypeEnum>(reader, "event_type", AuditEventTypeEnum.ConfigurationChanged);
            obj.RequestId = AdoConverters.NullableStr(reader, "request_id");
            obj.CorrelationId = AdoConverters.NullableStr(reader, "correlation_id");
            obj.TraceId = AdoConverters.NullableStr(reader, "trace_id");
            obj.AuthScheme = AdoConverters.NullableEnum<AuthSchemeEnum>(reader, "auth_scheme");
            obj.PrincipalType = AdoConverters.NullableEnum<PrincipalTypeEnum>(reader, "principal_type");
            obj.PrincipalId = AdoConverters.NullableStr(reader, "principal_id");
            obj.PrincipalName = AdoConverters.NullableStr(reader, "principal_name");
            obj.SessionId = AdoConverters.NullableStr(reader, "session_id");
            obj.ResourceType = AdoConverters.NullableStr(reader, "resource_type");
            obj.ResourceId = AdoConverters.NullableStr(reader, "resource_id");
            obj.Operation = AdoConverters.NullableStr(reader, "operation");
            obj.RequestType = AdoConverters.NullableStr(reader, "request_type");
            obj.Method = AdoConverters.NullableStr(reader, "method");
            obj.Path = AdoConverters.NullableStr(reader, "path");
            obj.SourceIp = AdoConverters.NullableStr(reader, "source_ip");
            obj.AuthResult = AdoConverters.NullableEnum<AuthResultEnum>(reader, "auth_result");
            obj.AuthorizationResult = AdoConverters.NullableEnum<AuthorizationResultEnum>(reader, "authorization_result");
            obj.Reason = AdoConverters.NullableStr(reader, "reason");
            obj.BypassReason = AdoConverters.NullableStr(reader, "bypass_reason");
            obj.StatusCode = AdoConverters.Int(reader, "status_code");
            obj.Description = AdoConverters.NullableStr(reader, "description");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.IsProtected = AdoConverters.Bool(reader, "is_protected");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        /// <inheritdoc />
        public async Task<AuditPage> EnumerateAsync(AuditFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            int pageSize = Math.Clamp(filter.PageSize, 1, 1000);
            int pageNumber = Math.Max(1, filter.PageNumber);
            List<string> clauses = new List<string>();
            Action<DbCommand> bind = BuildFilter(filter, clauses);
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);
            List<AuditRecord> rows = await _Driver.QueryAsync(
                "SELECT * FROM " + _Table + where + " ORDER BY created_utc DESC, id DESC" + _Driver.Dialect.LimitOffsetClause() + ";",
                command =>
                {
                    bind(command);
                    AdoDatabaseDriver.Add(command, "max", pageSize);
                    AdoDatabaseDriver.Add(command, "skip", (pageNumber - 1) * pageSize);
                },
                Map,
                token).ConfigureAwait(false);

            AuditPage page = new AuditPage();
            page.PageNumber = pageNumber;
            page.PageSize = pageSize;
            page.TotalCount = total;
            page.TotalPages = (int)((total + pageSize - 1) / pageSize);
            page.Objects = rows;
            return page;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(AuditFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            List<string> clauses = new List<string>();
            Action<DbCommand> bind = BuildFilter(filter, clauses);
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;
            return await _Driver.NonQueryAsync("DELETE FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            return await _Driver.NonQueryAsync("DELETE FROM " + _Table + " WHERE created_utc < @cutoff;", command => AdoDatabaseDriver.Add(command, "cutoff", olderThanUtc), token).ConfigureAwait(false);
        }

        private static Action<DbCommand> BuildFilter(AuditFilter filter, List<string> clauses)
        {
            if (!String.IsNullOrEmpty(filter.TenantId)) clauses.Add("tenant_id = @tenant_id");
            if (filter.EventType.HasValue) clauses.Add("event_type = @event_type");
            if (!String.IsNullOrEmpty(filter.PrincipalId)) clauses.Add("principal_id = @principal_id");
            if (filter.Result.HasValue) clauses.Add("authorization_result = @result");
            if (filter.FromUtc.HasValue) clauses.Add("created_utc >= @fromutc");
            if (filter.ToUtc.HasValue) clauses.Add("created_utc < @toutc");
            return command =>
            {
                if (!String.IsNullOrEmpty(filter.TenantId)) AdoDatabaseDriver.Add(command, "tenant_id", filter.TenantId);
                if (filter.EventType.HasValue) AdoDatabaseDriver.Add(command, "event_type", filter.EventType.Value.ToString());
                if (!String.IsNullOrEmpty(filter.PrincipalId)) AdoDatabaseDriver.Add(command, "principal_id", filter.PrincipalId);
                if (filter.Result.HasValue) AdoDatabaseDriver.Add(command, "result", filter.Result.Value.ToString());
                if (filter.FromUtc.HasValue) AdoDatabaseDriver.Add(command, "fromutc", filter.FromUtc.Value);
                if (filter.ToUtc.HasValue) AdoDatabaseDriver.Add(command, "toutc", filter.ToUtc.Value);
            };
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, AuditRecord obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, event_type, request_id, correlation_id, trace_id, auth_scheme, principal_type, principal_id, principal_name, session_id, resource_type, resource_id, operation, request_type, method, path, source_ip, auth_result, authorization_result, reason, bypass_reason, status_code, description, active, is_protected, created_utc, last_update_utc) VALUES (@id, @tenant_id, @event_type, @request_id, @correlation_id, @trace_id, @auth_scheme, @principal_type, @principal_id, @principal_name, @session_id, @resource_type, @resource_id, @operation, @request_type, @method, @path, @source_ip, @auth_result, @authorization_result, @reason, @bypass_reason, @status_code, @description, @active, @is_protected, @created_utc, @last_update_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "event_type", obj.EventType.ToString());
                AdoDatabaseDriver.Add(command, "request_id", obj.RequestId);
                AdoDatabaseDriver.Add(command, "correlation_id", obj.CorrelationId);
                AdoDatabaseDriver.Add(command, "trace_id", obj.TraceId);
                AdoDatabaseDriver.Add(command, "auth_scheme", obj.AuthScheme?.ToString());
                AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType?.ToString());
                AdoDatabaseDriver.Add(command, "principal_id", obj.PrincipalId);
                AdoDatabaseDriver.Add(command, "principal_name", obj.PrincipalName);
                AdoDatabaseDriver.Add(command, "session_id", obj.SessionId);
                AdoDatabaseDriver.Add(command, "resource_type", obj.ResourceType);
                AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
                AdoDatabaseDriver.Add(command, "operation", obj.Operation);
                AdoDatabaseDriver.Add(command, "request_type", obj.RequestType);
                AdoDatabaseDriver.Add(command, "method", obj.Method);
                AdoDatabaseDriver.Add(command, "path", obj.Path);
                AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                AdoDatabaseDriver.Add(command, "auth_result", obj.AuthResult?.ToString());
                AdoDatabaseDriver.Add(command, "authorization_result", obj.AuthorizationResult?.ToString());
                AdoDatabaseDriver.Add(command, "reason", obj.Reason);
                AdoDatabaseDriver.Add(command, "bypass_reason", obj.BypassReason);
                AdoDatabaseDriver.Add(command, "status_code", obj.StatusCode);
                AdoDatabaseDriver.Add(command, "description", obj.Description);
                AdoDatabaseDriver.Add(command, "active", obj.Active);
                AdoDatabaseDriver.Add(command, "is_protected", obj.IsProtected);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
            }, token).ConfigureAwait(false);
        }

        #endregion
    }
}
