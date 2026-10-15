namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Provider-neutral approval data access. Thread-safe.
    /// </summary>
    public class AdoApprovalMethods : IApprovalMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private static readonly string _Columns = "id, tenant_id, tool_invocation_id, agent_profile_id, binding_id, binding_alias, resource_id, tool_name, surface, requested_by_principal_type, requested_by_principal_id, requested_by_name, summary, effect, effect_reason, arguments_redacted, status, decided_by_user_id, decided_by_name, decision_note, decided_utc, expires_utc, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoApprovalMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("approvals");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Approval> CreateAsync(Approval obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Approval?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> TryDecideAsync(string tenantId, string id, ApprovalStatusEnum status, string? decidedById, string? decidedByName, string? note, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (status == ApprovalStatusEnum.Pending) throw new ArgumentException("A decision must leave the Pending status.", nameof(status));
            DateTime now = DateTime.UtcNow;
            int affected = await _Driver.NonQueryAsync("UPDATE " + _Table + " SET status = @status, decided_by_user_id = @by, decided_by_name = @by_name, decision_note = @note, decided_utc = @now, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id AND status = @pending;", command =>
            {
                AdoDatabaseDriver.Add(command, "status", status.ToString());
                AdoDatabaseDriver.Add(command, "by", decidedById);
                AdoDatabaseDriver.Add(command, "by_name", decidedByName);
                AdoDatabaseDriver.Add(command, "note", note);
                AdoDatabaseDriver.Add(command, "now", now);
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
                AdoDatabaseDriver.Add(command, "pending", ApprovalStatusEnum.Pending.ToString());
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Approval>> EnumerateAsync(string tenantId, EnumerationQuery query, ToolInvocationFilter? filter = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            ToolInvocationFilter f = filter ?? new ToolInvocationFilter();
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("summary", "search"));
            if (f.ApprovalStatus.HasValue) clauses.Add("status = @status");
            if (!String.IsNullOrEmpty(f.AgentProfileId)) clauses.Add("agent_profile_id = @agent_profile_id");
            if (!String.IsNullOrEmpty(f.ResourceId)) clauses.Add("resource_id = @resource_id");
            if (!String.IsNullOrEmpty(f.PrincipalId)) clauses.Add("requested_by_principal_id = @principal_id");
            if (!String.IsNullOrEmpty(f.ToolName)) clauses.Add("tool_name = @tool_name");
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
                if (f.ApprovalStatus.HasValue) AdoDatabaseDriver.Add(command, "status", f.ApprovalStatus.Value.ToString());
                if (!String.IsNullOrEmpty(f.AgentProfileId)) AdoDatabaseDriver.Add(command, "agent_profile_id", f.AgentProfileId);
                if (!String.IsNullOrEmpty(f.ResourceId)) AdoDatabaseDriver.Add(command, "resource_id", f.ResourceId);
                if (!String.IsNullOrEmpty(f.PrincipalId)) AdoDatabaseDriver.Add(command, "principal_id", f.PrincipalId);
                if (!String.IsNullOrEmpty(f.ToolName)) AdoDatabaseDriver.Add(command, "tool_name", f.ToolName);
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<Approval> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<Approval>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<Approval>> ListExpiredAsync(DateTime nowUtc, int maxResults, CancellationToken token = default)
        {
            int max = Math.Clamp(maxResults, 1, 1000);
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE status = @pending AND expires_utc < @now ORDER BY expires_utc ASC" + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                AdoDatabaseDriver.Add(command, "pending", ApprovalStatusEnum.Pending.ToString());
                AdoDatabaseDriver.Add(command, "now", nowUtc);
                AdoDatabaseDriver.Add(command, "max", max);
                AdoDatabaseDriver.Add(command, "skip", 0);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Approval>> ListPendingAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND status = @pending ORDER BY created_utc ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "pending", ApprovalStatusEnum.Pending.ToString());
            }, Map, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to an approval.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Approval.</returns>
        public static Approval Map(DbDataReader reader)
        {
            Approval obj = new Approval();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.ToolInvocationId = AdoConverters.Str(reader, "tool_invocation_id");
            obj.AgentProfileId = AdoConverters.NullableStr(reader, "agent_profile_id");
            obj.BindingId = AdoConverters.NullableStr(reader, "binding_id");
            obj.BindingAlias = AdoConverters.NullableStr(reader, "binding_alias");
            obj.ResourceId = AdoConverters.NullableStr(reader, "resource_id");
            obj.ToolName = AdoConverters.Str(reader, "tool_name");
            obj.Surface = AdoConverters.Enum(reader, "surface", InvocationSurfaceEnum.Rest);
            obj.RequestedByPrincipalType = AdoConverters.Enum(reader, "requested_by_principal_type", PrincipalTypeEnum.User);
            obj.RequestedByPrincipalId = AdoConverters.NullableStr(reader, "requested_by_principal_id");
            obj.RequestedByName = AdoConverters.NullableStr(reader, "requested_by_name");
            obj.Summary = AdoConverters.Str(reader, "summary");
            obj.Effect = AdoConverters.Enum(reader, "effect", EffectEnum.Write);
            obj.EffectReason = AdoConverters.NullableStr(reader, "effect_reason");
            obj.ArgumentsRedacted = AdoConverters.NullableStr(reader, "arguments_redacted");
            obj.Status = AdoConverters.Enum(reader, "status", ApprovalStatusEnum.Pending);
            obj.DecidedByUserId = AdoConverters.NullableStr(reader, "decided_by_user_id");
            obj.DecidedByName = AdoConverters.NullableStr(reader, "decided_by_name");
            obj.DecisionNote = AdoConverters.NullableStr(reader, "decision_note");
            obj.DecidedUtc = AdoConverters.NullableDate(reader, "decided_utc");
            obj.ExpiresUtc = AdoConverters.Date(reader, "expires_utc");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, Approval obj)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "tool_invocation_id", obj.ToolInvocationId);
            AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
            AdoDatabaseDriver.Add(command, "binding_id", obj.BindingId);
            AdoDatabaseDriver.Add(command, "binding_alias", obj.BindingAlias);
            AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
            AdoDatabaseDriver.Add(command, "tool_name", obj.ToolName);
            AdoDatabaseDriver.Add(command, "surface", obj.Surface.ToString());
            AdoDatabaseDriver.Add(command, "requested_by_principal_type", obj.RequestedByPrincipalType.ToString());
            AdoDatabaseDriver.Add(command, "requested_by_principal_id", obj.RequestedByPrincipalId);
            AdoDatabaseDriver.Add(command, "requested_by_name", obj.RequestedByName);
            AdoDatabaseDriver.Add(command, "summary", obj.Summary);
            AdoDatabaseDriver.Add(command, "effect", obj.Effect.ToString());
            AdoDatabaseDriver.Add(command, "effect_reason", obj.EffectReason);
            AdoDatabaseDriver.Add(command, "arguments_redacted", obj.ArgumentsRedacted);
            AdoDatabaseDriver.Add(command, "status", obj.Status.ToString());
            AdoDatabaseDriver.Add(command, "decided_by_user_id", obj.DecidedByUserId);
            AdoDatabaseDriver.Add(command, "decided_by_name", obj.DecidedByName);
            AdoDatabaseDriver.Add(command, "decision_note", obj.DecisionNote);
            AdoDatabaseDriver.Add(command, "decided_utc", obj.DecidedUtc);
            AdoDatabaseDriver.Add(command, "expires_utc", obj.ExpiresUtc);
            AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
