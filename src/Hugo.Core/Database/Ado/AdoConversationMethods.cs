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
    using Hugo.Core.Requests;

    /// <summary>
    /// Provider-neutral conversation data access. Thread-safe.
    /// </summary>
    public class AdoConversationMethods : IConversationMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private static readonly string _Columns = "id, tenant_id, user_id, credential_id, agent_profile_id, title, source, a2a_context_id, external_key, message_count, last_message_utc, total_input_tokens, total_output_tokens, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoConversationMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("conversations");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Conversation> CreateAsync(Conversation obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Conversation?> ReadAsync(string tenantId, string id, CancellationToken token = default)
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
        public async Task<Conversation?> ReadByA2AContextAsync(string tenantId, string agentProfileId, string contextId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(agentProfileId)) throw new ArgumentNullException(nameof(agentProfileId));
            if (String.IsNullOrEmpty(contextId)) throw new ArgumentNullException(nameof(contextId));
            List<Conversation> found = await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND agent_profile_id = @profile AND a2a_context_id = @context ORDER BY created_utc ASC" + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "profile", agentProfileId);
                AdoDatabaseDriver.Add(command, "context", contextId);
                AdoDatabaseDriver.Add(command, "max", 1);
                AdoDatabaseDriver.Add(command, "skip", 0);
            }, Map, token).ConfigureAwait(false);
            return found.Count > 0 ? found[0] : null;
        }

        /// <inheritdoc />
        public async Task<Conversation?> ReadByExternalKeyAsync(string tenantId, string agentProfileId, string? userId, string? credentialId, string externalKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(agentProfileId)) throw new ArgumentNullException(nameof(agentProfileId));
            if (String.IsNullOrEmpty(externalKey)) throw new ArgumentNullException(nameof(externalKey));
            string where = "tenant_id = @tenant_id AND agent_profile_id = @profile AND external_key = @key"
                + (userId == null ? " AND user_id IS NULL" : " AND user_id = @user")
                + (credentialId == null ? " AND credential_id IS NULL" : " AND credential_id = @credential");
            List<Conversation> found = await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE " + where + " ORDER BY created_utc ASC" + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "profile", agentProfileId);
                AdoDatabaseDriver.Add(command, "key", externalKey);
                if (userId != null) AdoDatabaseDriver.Add(command, "user", userId);
                if (credentialId != null) AdoDatabaseDriver.Add(command, "credential", credentialId);
                AdoDatabaseDriver.Add(command, "max", 1);
                AdoDatabaseDriver.Add(command, "skip", 0);
            }, Map, token).ConfigureAwait(false);
            return found.Count > 0 ? found[0] : null;
        }

        /// <inheritdoc />
        public async Task<Conversation> UpdateAsync(Conversation obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET title = @title, a2a_context_id = @a2a_context_id, external_key = @external_key, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "title", obj.Title);
                AdoDatabaseDriver.Add(command, "a2a_context_id", obj.A2AContextId);
                AdoDatabaseDriver.Add(command, "external_key", obj.ExternalKey);
                AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
                AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                AdoDatabaseDriver.Add(command, "id", obj.Id);
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task AddUsageAsync(string tenantId, string id, long inputTokens, long outputTokens, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET total_input_tokens = total_input_tokens + @input, total_output_tokens = total_output_tokens + @output, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "input", Math.Max(0, inputTokens));
                AdoDatabaseDriver.Add(command, "output", Math.Max(0, outputTokens));
                AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int affected = 0;
            string calls = _Driver.Table("message_tool_calls");
            string messages = _Driver.Table("messages");
            string tasks = _Driver.Table("a2a_tasks");
            string artifacts = _Driver.Table("a2a_task_artifacts");
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                Action<DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "id", id);
                };
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + calls + " WHERE tenant_id = @tenant_id AND conversation_id = @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + messages + " WHERE tenant_id = @tenant_id AND conversation_id = @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + artifacts + " WHERE tenant_id = @tenant_id AND task_id IN (SELECT id FROM " + tasks + " WHERE tenant_id = @tenant_id AND conversation_id = @id);", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + tasks + " WHERE tenant_id = @tenant_id AND conversation_id = @id;", bind, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Conversation>> EnumerateAsync(string tenantId, EnumerationQuery query, ConversationFilter? filter = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("title", "search"));
            if (filter != null)
            {
                if (filter.UserId != null) clauses.Add("user_id = @user_id");
                if (filter.CredentialId != null) clauses.Add("credential_id = @credential_id");
                if (filter.AgentProfileId != null) clauses.Add("agent_profile_id = @agent_profile_id");
                if (filter.Source.HasValue) clauses.Add("source = @source");
            }
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
                if (filter != null)
                {
                    if (filter.UserId != null) AdoDatabaseDriver.Add(command, "user_id", filter.UserId);
                    if (filter.CredentialId != null) AdoDatabaseDriver.Add(command, "credential_id", filter.CredentialId);
                    if (filter.AgentProfileId != null) AdoDatabaseDriver.Add(command, "agent_profile_id", filter.AgentProfileId);
                    if (filter.Source.HasValue) AdoDatabaseDriver.Add(command, "source", filter.Source.Value.ToString());
                }
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            string order = query.Ordering == EnumerationOrderEnum.CreatedAscending ? " ORDER BY created_utc ASC, id ASC"
                : query.Ordering == EnumerationOrderEnum.CreatedDescending ? " ORDER BY last_update_utc DESC, id DESC"
                : AdoEnumerationSql.OrderBy(query.Ordering, false);
            List<Conversation> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + order + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<Conversation>.Build(query, total, objects);
        }

        /// <summary>
        /// Map a row to a conversation.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Conversation.</returns>
        public static Conversation Map(DbDataReader reader)
        {
            Conversation obj = new Conversation();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.UserId = AdoConverters.NullableStr(reader, "user_id");
            obj.CredentialId = AdoConverters.NullableStr(reader, "credential_id");
            obj.AgentProfileId = AdoConverters.Str(reader, "agent_profile_id");
            obj.Title = AdoConverters.NullableStr(reader, "title");
            obj.Source = AdoConverters.Enum(reader, "source", ConversationSourceEnum.Dashboard);
            obj.A2AContextId = AdoConverters.NullableStr(reader, "a2a_context_id");
            obj.ExternalKey = AdoConverters.NullableStr(reader, "external_key");
            obj.MessageCount = AdoConverters.Int(reader, "message_count");
            obj.LastMessageUtc = AdoConverters.NullableDate(reader, "last_message_utc");
            obj.TotalInputTokens = AdoConverters.Long(reader, "total_input_tokens");
            obj.TotalOutputTokens = AdoConverters.Long(reader, "total_output_tokens");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, Conversation obj)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "user_id", obj.UserId);
            AdoDatabaseDriver.Add(command, "credential_id", obj.CredentialId);
            AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
            AdoDatabaseDriver.Add(command, "title", obj.Title);
            AdoDatabaseDriver.Add(command, "source", obj.Source.ToString());
            AdoDatabaseDriver.Add(command, "a2a_context_id", obj.A2AContextId);
            AdoDatabaseDriver.Add(command, "external_key", obj.ExternalKey);
            AdoDatabaseDriver.Add(command, "message_count", obj.MessageCount);
            AdoDatabaseDriver.Add(command, "last_message_utc", obj.LastMessageUtc);
            AdoDatabaseDriver.Add(command, "total_input_tokens", obj.TotalInputTokens);
            AdoDatabaseDriver.Add(command, "total_output_tokens", obj.TotalOutputTokens);
            AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
