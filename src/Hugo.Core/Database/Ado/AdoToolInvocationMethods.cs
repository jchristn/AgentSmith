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
    using Hugo.Core.Responses;

    /// <summary>
    /// Provider-neutral tool invocation data access. Thread-safe.
    /// </summary>
    public class AdoToolInvocationMethods : IToolInvocationMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private static readonly string _Columns = "id, tenant_id, conversation_id, message_id, surface, principal_type, principal_id, principal_name, agent_profile_id, binding_id, binding_alias, resource_id, connector_type, tool_name, arguments_redacted, redaction_applied, effect, effect_reason, access_mode, decision, decision_code, decision_reason, approval_id, status, error_code, error_message, output_bytes, output_truncated, output_preview, duration_ms, trace_id, correlation_id, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoToolInvocationMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("tool_invocations");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ToolInvocation> CreateAsync(ToolInvocation obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj, true), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<ToolInvocation> UpdateAsync(ToolInvocation obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            List<string> sets = new List<string>();
            foreach (string column in _Columns.Split(", "))
            {
                if (column == "id" || column == "tenant_id" || column == "created_utc") continue;
                sets.Add(column + " = @" + column);
            }
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET " + String.Join(", ", sets) + " WHERE tenant_id = @tenant_id AND id = @id;", command => Bind(command, obj, false), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<ToolInvocation?> ReadAsync(string tenantId, string id, CancellationToken token = default)
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
        public async Task<bool> TryTransitionAsync(string tenantId, string id, InvocationStatusEnum from, InvocationStatusEnum to, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int affected = await _Driver.NonQueryAsync("UPDATE " + _Table + " SET status = @to, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id AND status = @from;", command =>
            {
                AdoDatabaseDriver.Add(command, "to", to.ToString());
                AdoDatabaseDriver.Add(command, "from", from.ToString());
                AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ToolInvocation>> EnumerateAsync(string tenantId, EnumerationQuery query, ToolInvocationFilter? filter = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            ToolInvocationFilter f = filter ?? new ToolInvocationFilter();
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("tool_name", "search"));
            if (!String.IsNullOrEmpty(f.AgentProfileId)) clauses.Add("agent_profile_id = @agent_profile_id");
            if (!String.IsNullOrEmpty(f.ResourceId)) clauses.Add("resource_id = @resource_id");
            if (!String.IsNullOrEmpty(f.ToolName)) clauses.Add("tool_name = @tool_name");
            if (!String.IsNullOrEmpty(f.ConversationId)) clauses.Add("conversation_id = @conversation_id");
            if (!String.IsNullOrEmpty(f.PrincipalId)) clauses.Add("principal_id = @principal_id");
            if (f.Decision.HasValue) clauses.Add("decision = @decision");
            if (f.Status.HasValue) clauses.Add("status = @status");
            if (f.Surface.HasValue) clauses.Add("surface = @surface");
            if (f.SinceUtc.HasValue) clauses.Add("created_utc >= @since");
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
                if (!String.IsNullOrEmpty(f.AgentProfileId)) AdoDatabaseDriver.Add(command, "agent_profile_id", f.AgentProfileId);
                if (!String.IsNullOrEmpty(f.ResourceId)) AdoDatabaseDriver.Add(command, "resource_id", f.ResourceId);
                if (!String.IsNullOrEmpty(f.ToolName)) AdoDatabaseDriver.Add(command, "tool_name", f.ToolName);
                if (!String.IsNullOrEmpty(f.ConversationId)) AdoDatabaseDriver.Add(command, "conversation_id", f.ConversationId);
                if (!String.IsNullOrEmpty(f.PrincipalId)) AdoDatabaseDriver.Add(command, "principal_id", f.PrincipalId);
                if (f.Decision.HasValue) AdoDatabaseDriver.Add(command, "decision", f.Decision.Value.ToString());
                if (f.Status.HasValue) AdoDatabaseDriver.Add(command, "status", f.Status.Value.ToString());
                if (f.Surface.HasValue) AdoDatabaseDriver.Add(command, "surface", f.Surface.Value.ToString());
                if (f.SinceUtc.HasValue) AdoDatabaseDriver.Add(command, "since", f.SinceUtc.Value);
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<ToolInvocation> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<ToolInvocation>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<ToolInvocationSummary> SummaryAsync(string tenantId, DateTime? sinceUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            ToolInvocationSummary summary = new ToolInvocationSummary();
            summary.SinceUtc = sinceUtc;
            string where = " WHERE tenant_id = @tenant_id" + (sinceUtc.HasValue ? " AND created_utc >= @since" : String.Empty);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (sinceUtc.HasValue) AdoDatabaseDriver.Add(command, "since", sinceUtc.Value);
            };
            summary.ByDecision = await GroupAsync("decision", where, bind, token).ConfigureAwait(false);
            summary.ByStatus = await GroupAsync("status", where, bind, token).ConfigureAwait(false);
            summary.ByEffect = await GroupAsync("effect", where, bind, token).ConfigureAwait(false);
            summary.ByTool = await GroupAsync("tool_name", where, bind, token).ConfigureAwait(false);
            long total = 0;
            foreach (StatusCount count in summary.ByDecision) total += count.Count;
            summary.Total = total;
            return summary;
        }

        /// <summary>
        /// Map a row to a tool invocation.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Record.</returns>
        public static ToolInvocation Map(DbDataReader reader)
        {
            ToolInvocation obj = new ToolInvocation();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.ConversationId = AdoConverters.NullableStr(reader, "conversation_id");
            obj.MessageId = AdoConverters.NullableStr(reader, "message_id");
            obj.Surface = AdoConverters.Enum(reader, "surface", InvocationSurfaceEnum.Rest);
            obj.PrincipalType = AdoConverters.Enum(reader, "principal_type", PrincipalTypeEnum.User);
            obj.PrincipalId = AdoConverters.NullableStr(reader, "principal_id");
            obj.PrincipalName = AdoConverters.NullableStr(reader, "principal_name");
            obj.AgentProfileId = AdoConverters.NullableStr(reader, "agent_profile_id");
            obj.BindingId = AdoConverters.NullableStr(reader, "binding_id");
            obj.BindingAlias = AdoConverters.NullableStr(reader, "binding_alias");
            obj.ResourceId = AdoConverters.NullableStr(reader, "resource_id");
            obj.ConnectorType = AdoConverters.NullableStr(reader, "connector_type");
            obj.ToolName = AdoConverters.Str(reader, "tool_name");
            obj.ArgumentsRedacted = AdoConverters.NullableStr(reader, "arguments_redacted");
            obj.RedactionApplied = AdoConverters.Bool(reader, "redaction_applied");
            obj.Effect = AdoConverters.NullableEnum<EffectEnum>(reader, "effect");
            obj.EffectReason = AdoConverters.NullableStr(reader, "effect_reason");
            obj.AccessMode = AdoConverters.NullableEnum<AccessModeEnum>(reader, "access_mode");
            obj.Decision = AdoConverters.Enum(reader, "decision", InvocationDecisionEnum.Denied);
            obj.DecisionCode = AdoConverters.NullableStr(reader, "decision_code");
            obj.DecisionReason = AdoConverters.NullableStr(reader, "decision_reason");
            obj.ApprovalId = AdoConverters.NullableStr(reader, "approval_id");
            obj.Status = AdoConverters.Enum(reader, "status", InvocationStatusEnum.NotExecuted);
            obj.ErrorCode = AdoConverters.NullableStr(reader, "error_code");
            obj.ErrorMessage = AdoConverters.NullableStr(reader, "error_message");
            obj.OutputBytes = AdoConverters.Long(reader, "output_bytes");
            obj.OutputTruncated = AdoConverters.Bool(reader, "output_truncated");
            obj.OutputPreview = AdoConverters.NullableStr(reader, "output_preview");
            obj.DurationMs = AdoConverters.Long(reader, "duration_ms");
            obj.TraceId = AdoConverters.NullableStr(reader, "trace_id");
            obj.CorrelationId = AdoConverters.NullableStr(reader, "correlation_id");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private async Task<List<StatusCount>> GroupAsync(string column, string where, Action<DbCommand> bind, CancellationToken token)
        {
            return await _Driver.QueryAsync("SELECT " + column + " AS k, COUNT(*) AS c FROM " + _Table + where + " GROUP BY " + column + " ORDER BY COUNT(*) DESC;", bind, reader =>
            {
                StatusCount count = new StatusCount();
                count.Status = AdoConverters.NullableStr(reader, "k") ?? "None";
                count.Count = AdoConverters.Long(reader, "c");
                return count;
            }, token).ConfigureAwait(false);
        }

        private static void Bind(DbCommand command, ToolInvocation obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "conversation_id", obj.ConversationId);
            AdoDatabaseDriver.Add(command, "message_id", obj.MessageId);
            AdoDatabaseDriver.Add(command, "surface", obj.Surface.ToString());
            AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType.ToString());
            AdoDatabaseDriver.Add(command, "principal_id", obj.PrincipalId);
            AdoDatabaseDriver.Add(command, "principal_name", obj.PrincipalName);
            AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
            AdoDatabaseDriver.Add(command, "binding_id", obj.BindingId);
            AdoDatabaseDriver.Add(command, "binding_alias", obj.BindingAlias);
            AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
            AdoDatabaseDriver.Add(command, "connector_type", obj.ConnectorType);
            AdoDatabaseDriver.Add(command, "tool_name", obj.ToolName);
            AdoDatabaseDriver.Add(command, "arguments_redacted", obj.ArgumentsRedacted);
            AdoDatabaseDriver.Add(command, "redaction_applied", obj.RedactionApplied);
            AdoDatabaseDriver.Add(command, "effect", obj.Effect?.ToString());
            AdoDatabaseDriver.Add(command, "effect_reason", obj.EffectReason);
            AdoDatabaseDriver.Add(command, "access_mode", obj.AccessMode?.ToString());
            AdoDatabaseDriver.Add(command, "decision", obj.Decision.ToString());
            AdoDatabaseDriver.Add(command, "decision_code", obj.DecisionCode);
            AdoDatabaseDriver.Add(command, "decision_reason", obj.DecisionReason);
            AdoDatabaseDriver.Add(command, "approval_id", obj.ApprovalId);
            AdoDatabaseDriver.Add(command, "status", obj.Status.ToString());
            AdoDatabaseDriver.Add(command, "error_code", obj.ErrorCode);
            AdoDatabaseDriver.Add(command, "error_message", obj.ErrorMessage);
            AdoDatabaseDriver.Add(command, "output_bytes", obj.OutputBytes);
            AdoDatabaseDriver.Add(command, "output_truncated", obj.OutputTruncated);
            AdoDatabaseDriver.Add(command, "output_preview", obj.OutputPreview);
            AdoDatabaseDriver.Add(command, "duration_ms", obj.DurationMs);
            AdoDatabaseDriver.Add(command, "trace_id", obj.TraceId);
            AdoDatabaseDriver.Add(command, "correlation_id", obj.CorrelationId);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
