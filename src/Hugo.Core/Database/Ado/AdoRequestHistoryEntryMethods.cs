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
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;

    /// <summary>
    /// Provider-neutral RequestHistoryEntry data access. Thread-safe.
    /// </summary>
    public class AdoRequestHistoryEntryMethods : IRequestHistoryEntryMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _HeadersTable;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoRequestHistoryEntryMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("request_history");
            _HeadersTable = driver.Table("request_history_headers");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<RequestHistoryEntry> CreateAsync(RequestHistoryEntry obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await InsertRowAsync(connection, transaction, obj, ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            return obj;
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            RequestHistoryEntry? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id;",
                command => AdoDatabaseDriver.Add(command, "id", id),
                Map,
                token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(obj, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            RequestHistoryEntry? obj = await _Driver.QuerySingleAsync(
                "SELECT * FROM " + _Table + " WHERE id = @id AND tenant_id = @tenant_id;",
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
        public async Task<RequestHistoryEntry> UpdateAsync(RequestHistoryEntry obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            string sql = "UPDATE " + _Table + " SET tenant_id = @tenant_id, user_id = @user_id, principal_name = @principal_name, method = @method, path = @path, url = @url, status_code = @status_code, duration_ms = @duration_ms, source_ip = @source_ip, trace_id = @trace_id, correlation_id = @correlation_id, request_body = @request_body, request_body_bytes = @request_body_bytes, request_body_truncated = @request_body_truncated, response_body = @response_body, response_body_bytes = @response_body_bytes, response_body_truncated = @response_body_truncated, completed_utc = @completed_utc WHERE id = @id;";

            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
                {
                    AdoDatabaseDriver.Add(command, "id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "user_id", obj.UserId);
                    AdoDatabaseDriver.Add(command, "principal_name", obj.PrincipalName);
                    AdoDatabaseDriver.Add(command, "method", obj.Method);
                    AdoDatabaseDriver.Add(command, "path", obj.Path);
                    AdoDatabaseDriver.Add(command, "url", obj.Url);
                    AdoDatabaseDriver.Add(command, "status_code", obj.StatusCode);
                    AdoDatabaseDriver.Add(command, "duration_ms", obj.DurationMs);
                    AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                    AdoDatabaseDriver.Add(command, "trace_id", obj.TraceId);
                    AdoDatabaseDriver.Add(command, "correlation_id", obj.CorrelationId);
                    AdoDatabaseDriver.Add(command, "request_body", obj.RequestBody);
                    AdoDatabaseDriver.Add(command, "request_body_bytes", obj.RequestBodyBytes);
                    AdoDatabaseDriver.Add(command, "request_body_truncated", obj.RequestBodyTruncated);
                    AdoDatabaseDriver.Add(command, "response_body", obj.ResponseBody);
                    AdoDatabaseDriver.Add(command, "response_body_bytes", obj.ResponseBodyBytes);
                    AdoDatabaseDriver.Add(command, "response_body_truncated", obj.ResponseBodyTruncated);
                    AdoDatabaseDriver.Add(command, "completed_utc", obj.CompletedUtc);
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
        public async Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<string> clauses = new List<string>();
            if (!String.IsNullOrEmpty(tenantId))
            {
                clauses.Add("tenant_id = @tenant_id");
            }
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            Action<DbCommand> bind = command =>
            {
                if (!String.IsNullOrEmpty(tenantId)) AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
            };

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string sql = "SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";";
            List<RequestHistoryEntry> objects = await _Driver.QueryAsync(sql, command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);

            foreach (RequestHistoryEntry obj in objects) await LoadChildrenAsync(obj, token).ConfigureAwait(false);

            return EnumerationResult<RequestHistoryEntry>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a RequestHistoryEntry (child rows not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>The record.</returns>
        public static RequestHistoryEntry Map(DbDataReader reader)
        {
            RequestHistoryEntry obj = new RequestHistoryEntry();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.NullableStr(reader, "tenant_id");
            obj.UserId = AdoConverters.NullableStr(reader, "user_id");
            obj.PrincipalName = AdoConverters.NullableStr(reader, "principal_name");
            obj.Method = AdoConverters.Str(reader, "method");
            obj.Path = AdoConverters.Str(reader, "path");
            obj.Url = AdoConverters.Str(reader, "url");
            obj.StatusCode = AdoConverters.Int(reader, "status_code");
            obj.DurationMs = AdoConverters.Double(reader, "duration_ms");
            obj.SourceIp = AdoConverters.NullableStr(reader, "source_ip");
            obj.TraceId = AdoConverters.NullableStr(reader, "trace_id");
            obj.CorrelationId = AdoConverters.NullableStr(reader, "correlation_id");
            obj.RequestBody = AdoConverters.NullableStr(reader, "request_body");
            obj.RequestBodyBytes = AdoConverters.Long(reader, "request_body_bytes");
            obj.RequestBodyTruncated = AdoConverters.Bool(reader, "request_body_truncated");
            obj.ResponseBody = AdoConverters.NullableStr(reader, "response_body");
            obj.ResponseBodyBytes = AdoConverters.Long(reader, "response_body_bytes");
            obj.ResponseBodyTruncated = AdoConverters.Bool(reader, "response_body_truncated");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.CompletedUtc = AdoConverters.NullableDate(reader, "completed_utc");
            return obj;
        }

        /// <summary>
        /// Load child rows into a RequestHistoryEntry.
        /// </summary>
        /// <param name="obj">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task LoadChildrenAsync(RequestHistoryEntry obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            obj.Headers = await _Driver.QueryAsync(
                "SELECT * FROM " + _HeadersTable + " WHERE request_history_entry_id = @parentid ORDER BY id ASC;",
                command => AdoDatabaseDriver.Add(command, "parentid", obj.Id),
                MapHeaders,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            int pageSize = Math.Clamp(filter.PageSize, 1, 1000);
            int pageNumber = Math.Max(1, filter.PageNumber);
            List<string> clauses = new List<string>();
            Action<DbCommand> bind = BuildFilter(filter, clauses);
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;

            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue);

            string columns = "id, tenant_id, user_id, principal_name, method, path, url, status_code, duration_ms, source_ip, NULL AS request_body, request_body_bytes, request_body_truncated, NULL AS response_body, response_body_bytes, response_body_truncated, created_utc, completed_utc";
            List<RequestHistoryEntry> rows = await _Driver.QueryAsync(
                "SELECT " + columns + " FROM " + _Table + where + " ORDER BY created_utc DESC, id DESC" + _Driver.Dialect.LimitOffsetClause() + ";",
                command =>
                {
                    bind(command);
                    AdoDatabaseDriver.Add(command, "max", pageSize);
                    AdoDatabaseDriver.Add(command, "skip", (pageNumber - 1) * pageSize);
                },
                Map,
                token).ConfigureAwait(false);

            RequestHistoryPage page = new RequestHistoryPage();
            page.PageNumber = pageNumber;
            page.PageSize = pageSize;
            page.TotalCount = total;
            page.TotalPages = (int)((total + pageSize - 1) / pageSize);
            page.Objects = rows;
            return page;
        }

        /// <inheritdoc />
        public async Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            DateTime to = filter.ToUtc ?? DateTime.UtcNow;
            DateTime from = filter.FromUtc ?? to.AddHours(-24);
            if (from >= to) from = to.AddMinutes(-1);
            int bucketMinutes = Math.Clamp(filter.BucketMinutes, 1, 10080);
            TimeSpan size = TimeSpan.FromMinutes(bucketMinutes);
            long bucketCount = (long)Math.Ceiling((to - from).Ticks / (double)size.Ticks);
            if (bucketCount > 10000) throw new ArgumentException("The range produces more than 10000 buckets; increase bucketMinutes.");

            RequestHistoryFilter scoped = new RequestHistoryFilter();
            scoped.TenantId = filter.TenantId;
            scoped.UserId = filter.UserId;
            scoped.Method = filter.Method;
            scoped.StatusCode = filter.StatusCode;
            scoped.PathContains = filter.PathContains;
            scoped.FromUtc = from;
            scoped.ToUtc = to;
            List<string> clauses = new List<string>();
            Action<DbCommand> bind = BuildFilter(scoped, clauses);
            string where = " WHERE " + String.Join(" AND ", clauses);

            List<RequestHistoryBucket> buckets = new List<RequestHistoryBucket>();
            List<double> durationSums = new List<double>();
            for (long i = 0; i < bucketCount; i++)
            {
                RequestHistoryBucket bucket = new RequestHistoryBucket();
                bucket.BucketStartUtc = from.Add(TimeSpan.FromTicks(size.Ticks * i));
                bucket.BucketEndUtc = bucket.BucketStartUtc.Add(size);
                buckets.Add(bucket);
                durationSums.Add(0);
            }

            RequestHistorySummary summary = new RequestHistorySummary();
            double totalDuration = 0;
            await _Driver.QueryAsync<int>(
                "SELECT created_utc, status_code, duration_ms FROM " + _Table + where + ";",
                bind,
                reader =>
                {
                    DateTime created = AdoConverters.Date(reader, "created_utc");
                    int status = AdoConverters.Int(reader, "status_code");
                    double duration = AdoConverters.Double(reader, "duration_ms");
                    long index = (created - from).Ticks / size.Ticks;
                    if (index < 0 || index >= buckets.Count) return 0;
                    RequestHistoryBucket bucket = buckets[(int)index];
                    if (status >= 400)
                    {
                        bucket.FailureCount++;
                        summary.TotalFailure++;
                    }
                    else
                    {
                        bucket.SuccessCount++;
                        summary.TotalSuccess++;
                    }
                    durationSums[(int)index] += duration;
                    totalDuration += duration;
                    return 0;
                },
                token).ConfigureAwait(false);

            for (int i = 0; i < buckets.Count; i++)
            {
                long count = buckets[i].SuccessCount + buckets[i].FailureCount;
                buckets[i].AverageDurationMs = count > 0 ? Math.Round(durationSums[i] / count, 2) : 0;
            }
            summary.TotalCount = summary.TotalSuccess + summary.TotalFailure;
            summary.AverageDurationMs = summary.TotalCount > 0 ? Math.Round(totalDuration / summary.TotalCount, 2) : 0;
            summary.Buckets = buckets;
            return summary;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            List<string> clauses = new List<string>();
            Action<DbCommand> bind = BuildFilter(filter, clauses);
            string where = clauses.Count > 0 ? " WHERE " + String.Join(" AND ", clauses) : String.Empty;
            return await DeleteWhereAsync(where, bind, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            return await DeleteWhereAsync(" WHERE created_utc < @cutoff", command => AdoDatabaseDriver.Add(command, "cutoff", olderThanUtc), token).ConfigureAwait(false);
        }

        private async Task<int> DeleteWhereAsync(string where, Action<DbCommand> bind, CancellationToken token)
        {
            int affected = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "DELETE FROM " + _HeadersTable + " WHERE request_history_entry_id IN (SELECT id FROM " + _Table + where + ");", bind, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + where + ";", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected;
        }

        private Action<DbCommand> BuildFilter(RequestHistoryFilter filter, List<string> clauses)
        {
            if (!String.IsNullOrEmpty(filter.TenantId)) clauses.Add("tenant_id = @tenant_id");
            if (!String.IsNullOrEmpty(filter.UserId)) clauses.Add("user_id = @user_id");
            if (!String.IsNullOrEmpty(filter.Method)) clauses.Add("method = @method");
            if (filter.StatusCode.HasValue) clauses.Add("status_code = @status_code");
            if (!String.IsNullOrEmpty(filter.PathContains)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("path", "pathcontains"));
            if (filter.FromUtc.HasValue) clauses.Add("created_utc >= @fromutc");
            if (filter.ToUtc.HasValue) clauses.Add("created_utc < @toutc");
            return command =>
            {
                if (!String.IsNullOrEmpty(filter.TenantId)) AdoDatabaseDriver.Add(command, "tenant_id", filter.TenantId);
                if (!String.IsNullOrEmpty(filter.UserId)) AdoDatabaseDriver.Add(command, "user_id", filter.UserId);
                if (!String.IsNullOrEmpty(filter.Method)) AdoDatabaseDriver.Add(command, "method", filter.Method.ToUpperInvariant());
                if (filter.StatusCode.HasValue) AdoDatabaseDriver.Add(command, "status_code", filter.StatusCode.Value);
                if (!String.IsNullOrEmpty(filter.PathContains)) AdoDatabaseDriver.Add(command, "pathcontains", "%" + filter.PathContains + "%");
                if (filter.FromUtc.HasValue) AdoDatabaseDriver.Add(command, "fromutc", filter.FromUtc.Value);
                if (filter.ToUtc.HasValue) AdoDatabaseDriver.Add(command, "toutc", filter.ToUtc.Value);
            };
        }

        #endregion

        #region Private-Methods

        private async Task InsertRowAsync(DbConnection connection, DbTransaction transaction, RequestHistoryEntry obj, CancellationToken token)
        {
            string sql = "INSERT INTO " + _Table + " (id, tenant_id, user_id, principal_name, method, path, url, status_code, duration_ms, source_ip, trace_id, correlation_id, request_body, request_body_bytes, request_body_truncated, response_body, response_body_bytes, response_body_truncated, created_utc, completed_utc) VALUES (@id, @tenant_id, @user_id, @principal_name, @method, @path, @url, @status_code, @duration_ms, @source_ip, @trace_id, @correlation_id, @request_body, @request_body_bytes, @request_body_truncated, @response_body, @response_body_bytes, @response_body_truncated, @created_utc, @completed_utc);";
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, sql, command =>
            {
                AdoDatabaseDriver.Add(command, "id", obj.Id);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "user_id", obj.UserId);
                AdoDatabaseDriver.Add(command, "principal_name", obj.PrincipalName);
                AdoDatabaseDriver.Add(command, "method", obj.Method);
                AdoDatabaseDriver.Add(command, "path", obj.Path);
                AdoDatabaseDriver.Add(command, "url", obj.Url);
                AdoDatabaseDriver.Add(command, "status_code", obj.StatusCode);
                AdoDatabaseDriver.Add(command, "duration_ms", obj.DurationMs);
                AdoDatabaseDriver.Add(command, "source_ip", obj.SourceIp);
                AdoDatabaseDriver.Add(command, "trace_id", obj.TraceId);
                AdoDatabaseDriver.Add(command, "correlation_id", obj.CorrelationId);
                AdoDatabaseDriver.Add(command, "request_body", obj.RequestBody);
                AdoDatabaseDriver.Add(command, "request_body_bytes", obj.RequestBodyBytes);
                AdoDatabaseDriver.Add(command, "request_body_truncated", obj.RequestBodyTruncated);
                AdoDatabaseDriver.Add(command, "response_body", obj.ResponseBody);
                AdoDatabaseDriver.Add(command, "response_body_bytes", obj.ResponseBodyBytes);
                AdoDatabaseDriver.Add(command, "response_body_truncated", obj.ResponseBodyTruncated);
                AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
                AdoDatabaseDriver.Add(command, "completed_utc", obj.CompletedUtc);
            }, token).ConfigureAwait(false);
        }

        private async Task InsertChildrenAsync(DbConnection connection, DbTransaction transaction, RequestHistoryEntry obj, CancellationToken token)
        {
            string batch = IdGenerator.NewChildRowId() + "_";
            for (int i = 0; i < obj.Headers.Count; i++)
            {
                RequestHistoryHeader child = obj.Headers[i];
                child.Id = batch + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "hea";
                child.RequestHistoryEntryId = obj.Id;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _HeadersTable + " (id, request_history_entry_id, tenant_id, direction, name, value) VALUES (@id, @request_history_entry_id, @tenant_id, @direction, @name, @value);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", child.Id);
                        AdoDatabaseDriver.Add(command, "request_history_entry_id", obj.Id);
                        AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                        AdoDatabaseDriver.Add(command, "direction", child.Direction.ToString());
                        AdoDatabaseDriver.Add(command, "name", child.Name);
                        AdoDatabaseDriver.Add(command, "value", child.Value);
                    }, token).ConfigureAwait(false);
            }
        }

        private async Task DeleteChildrenAsync(DbConnection connection, DbTransaction transaction, string parentId, CancellationToken token)
        {
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _HeadersTable + " WHERE request_history_entry_id = @parentid;", command => AdoDatabaseDriver.Add(command, "parentid", parentId), token).ConfigureAwait(false);
        }

        private static RequestHistoryHeader MapHeaders(DbDataReader reader)
        {
            RequestHistoryHeader child = new RequestHistoryHeader();
            child.Id = AdoConverters.Str(reader, "id");
            child.RequestHistoryEntryId = AdoConverters.Str(reader, "request_history_entry_id");
            child.Direction = AdoConverters.Enum<HeaderDirectionEnum>(reader, "direction", HeaderDirectionEnum.Request);
            child.Name = AdoConverters.Str(reader, "name");
            child.Value = AdoConverters.Str(reader, "value");
            return child;
        }

        #endregion
    }
}
