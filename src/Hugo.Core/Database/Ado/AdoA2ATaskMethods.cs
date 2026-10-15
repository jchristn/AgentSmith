namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral A2A task and artifact data access. Thread-safe.
    /// </summary>
    public class AdoA2ATaskMethods : IA2ATaskMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Artifacts;
        private static readonly string _Columns = "id, tenant_id, agent_profile_id, principal_type, principal_id, conversation_id, context_id, message_id, state, status_message, pending_approval_id, input_tokens, output_tokens, tool_call_count, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoA2ATaskMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("a2a_tasks");
            _Artifacts = driver.Table("a2a_task_artifacts");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<A2ATask> CreateAsync(A2ATask obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<A2ATask?> ReadAsync(string tenantId, string id, CancellationToken token = default)
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
        public async Task<A2ATask> UpdateAsync(A2ATask obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET state = @state, status_message = @status_message, pending_approval_id = @pending_approval_id, input_tokens = @input_tokens, output_tokens = @output_tokens, tool_call_count = @tool_call_count, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;", command => Bind(command, obj), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<A2ATask>> EnumerateAsync(string tenantId, string agentProfileId, string? principalId, string? contextId, A2ATaskStateEnum? state, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(agentProfileId)) throw new ArgumentNullException(nameof(agentProfileId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> clauses = new List<string> { "tenant_id = @tenant_id", "agent_profile_id = @profile" };
            if (principalId != null) clauses.Add("principal_id = @principal");
            if (!String.IsNullOrEmpty(contextId)) clauses.Add("context_id = @context");
            if (state.HasValue) clauses.Add("state = @state");
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "profile", agentProfileId);
                if (principalId != null) AdoDatabaseDriver.Add(command, "principal", principalId);
                if (!String.IsNullOrEmpty(contextId)) AdoDatabaseDriver.Add(command, "context", contextId);
                if (state.HasValue) AdoDatabaseDriver.Add(command, "state", state.Value.ToString());
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<A2ATask> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, false) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<A2ATask>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<A2ATask>> ListByStateAsync(List<A2ATaskStateEnum> states, CancellationToken token = default)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            if (states.Count == 0) return new List<A2ATask>();
            List<string> names = new List<string>();
            for (int i = 0; i < states.Count; i++) names.Add("@s" + i);
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE state IN (" + String.Join(", ", names) + ") ORDER BY created_utc ASC;", command =>
            {
                for (int i = 0; i < states.Count; i++) AdoDatabaseDriver.Add(command, "s" + i, states[i].ToString());
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<A2ATaskArtifact> AddArtifactAsync(A2ATaskArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.CreatedUtc = DateTime.UtcNow;
            await _Driver.NonQueryAsync("INSERT INTO " + _Artifacts + " (id, task_id, tenant_id, artifact_id, name, mime_type, content, created_utc) VALUES (@id, @task_id, @tenant_id, @artifact_id, @name, @mime_type, @content, @created_utc);", command =>
            {
                AdoDatabaseDriver.Add(command, "id", artifact.Id);
                AdoDatabaseDriver.Add(command, "task_id", artifact.TaskId);
                AdoDatabaseDriver.Add(command, "tenant_id", artifact.TenantId);
                AdoDatabaseDriver.Add(command, "artifact_id", artifact.ArtifactId);
                AdoDatabaseDriver.Add(command, "name", artifact.Name);
                AdoDatabaseDriver.Add(command, "mime_type", artifact.MimeType);
                AdoDatabaseDriver.Add(command, "content", artifact.Content);
                AdoDatabaseDriver.Add(command, "created_utc", artifact.CreatedUtc);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<List<A2ATaskArtifact>> ListArtifactsAsync(string tenantId, string taskId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Artifacts + " WHERE tenant_id = @tenant_id AND task_id = @task_id ORDER BY created_utc ASC, id ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "task_id", taskId);
            }, reader =>
            {
                A2ATaskArtifact artifact = new A2ATaskArtifact();
                artifact.Id = AdoConverters.Str(reader, "id");
                artifact.TaskId = AdoConverters.Str(reader, "task_id");
                artifact.TenantId = AdoConverters.Str(reader, "tenant_id");
                artifact.ArtifactId = AdoConverters.Str(reader, "artifact_id");
                artifact.Name = AdoConverters.NullableStr(reader, "name");
                artifact.MimeType = AdoConverters.Str(reader, "mime_type");
                artifact.Content = AdoConverters.Str(reader, "content");
                artifact.CreatedUtc = AdoConverters.Date(reader, "created_utc");
                return artifact;
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a task.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Task.</returns>
        public static A2ATask Map(DbDataReader reader)
        {
            A2ATask obj = new A2ATask();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.AgentProfileId = AdoConverters.Str(reader, "agent_profile_id");
            obj.PrincipalType = AdoConverters.Enum(reader, "principal_type", PrincipalTypeEnum.User);
            obj.PrincipalId = AdoConverters.NullableStr(reader, "principal_id");
            obj.ConversationId = AdoConverters.Str(reader, "conversation_id");
            obj.ContextId = AdoConverters.Str(reader, "context_id");
            obj.MessageId = AdoConverters.NullableStr(reader, "message_id");
            obj.State = AdoConverters.Enum(reader, "state", A2ATaskStateEnum.Failed);
            obj.StatusMessage = AdoConverters.NullableStr(reader, "status_message");
            obj.PendingApprovalId = AdoConverters.NullableStr(reader, "pending_approval_id");
            obj.InputTokens = AdoConverters.Long(reader, "input_tokens");
            obj.OutputTokens = AdoConverters.Long(reader, "output_tokens");
            obj.ToolCallCount = AdoConverters.Int(reader, "tool_call_count");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, A2ATask obj)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
            AdoDatabaseDriver.Add(command, "principal_type", obj.PrincipalType.ToString());
            AdoDatabaseDriver.Add(command, "principal_id", obj.PrincipalId);
            AdoDatabaseDriver.Add(command, "conversation_id", obj.ConversationId);
            AdoDatabaseDriver.Add(command, "context_id", obj.ContextId);
            AdoDatabaseDriver.Add(command, "message_id", obj.MessageId);
            AdoDatabaseDriver.Add(command, "state", obj.State.ToString());
            AdoDatabaseDriver.Add(command, "status_message", obj.StatusMessage);
            AdoDatabaseDriver.Add(command, "pending_approval_id", obj.PendingApprovalId);
            AdoDatabaseDriver.Add(command, "input_tokens", obj.InputTokens);
            AdoDatabaseDriver.Add(command, "output_tokens", obj.OutputTokens);
            AdoDatabaseDriver.Add(command, "tool_call_count", obj.ToolCallCount);
            AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
