namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Helpers;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral message data access with tool calls in <c>message_tool_calls</c>. Thread-safe.
    /// </summary>
    public class AdoMessageMethods : IMessageMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Calls;
        private readonly string _Conversations;
        private static readonly string _Columns = "id, tenant_id, conversation_id, turn_id, sequence, role, content, thinking_content, tool_call_id, tool_name, model_endpoint_id, input_tokens, output_tokens, time_to_first_token_ms, duration_ms, finish_reason, compacted, created_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoMessageMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("messages");
            _Calls = driver.Table("message_tool_calls");
            _Conversations = driver.Table("conversations");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> UpdateFeedbackAsync(string tenantId, string conversationId, string id, string? rating, string? comment, CancellationToken token = default)
        {
            int changed = await _Driver.NonQueryAsync("UPDATE " + _Table + " SET feedback_rating = @rating, feedback_comment = @comment, feedback_updated_utc = @updated WHERE tenant_id = @tenant AND conversation_id = @conversation AND id = @id AND role = 'Assistant';", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant", tenantId);
                AdoDatabaseDriver.Add(command, "conversation", conversationId);
                AdoDatabaseDriver.Add(command, "id", id);
                AdoDatabaseDriver.Add(command, "rating", rating);
                AdoDatabaseDriver.Add(command, "comment", comment);
                AdoDatabaseDriver.Add(command, "updated", DateTime.UtcNow);
            }, token).ConfigureAwait(false);
            return changed == 1;
        }


        /// <inheritdoc />
        public async Task AppendAsync(string tenantId, string conversationId, List<ConversationMessage> messages, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (messages.Count == 0) return;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                List<long> max = await AdoDatabaseDriver.QueryAsync(connection, transaction, "SELECT COALESCE(MAX(sequence), 0) AS max_sequence FROM " + _Table + " WHERE conversation_id = @conversation_id;",
                    command => AdoDatabaseDriver.Add(command, "conversation_id", conversationId),
                    reader => Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
                long sequence = max.Count > 0 ? max[0] : 0;
                DateTime last = DateTime.UtcNow;
                foreach (ConversationMessage message in messages)
                {
                    message.TenantId = tenantId;
                    message.ConversationId = conversationId;
                    message.Sequence = ++sequence;
                    last = message.CreatedUtc;
                    await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, message), ct).ConfigureAwait(false);
                    for (int i = 0; i < message.ToolCalls.Count; i++)
                    {
                        ConversationToolCall call = message.ToolCalls[i];
                        int ordinal = i;
                        await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Calls + " (id, message_id, conversation_id, tenant_id, ordinal, call_id, name, arguments_json) VALUES (@id, @message_id, @conversation_id, @tenant_id, @ordinal, @call_id, @name, @arguments_json);", command =>
                        {
                            AdoDatabaseDriver.Add(command, "id", IdGenerator.NewChildRowId());
                            AdoDatabaseDriver.Add(command, "message_id", message.Id);
                            AdoDatabaseDriver.Add(command, "conversation_id", conversationId);
                            AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                            AdoDatabaseDriver.Add(command, "ordinal", ordinal);
                            AdoDatabaseDriver.Add(command, "call_id", call.CallId);
                            AdoDatabaseDriver.Add(command, "name", call.Name);
                            AdoDatabaseDriver.Add(command, "arguments_json", call.ArgumentsJson);
                        }, ct).ConfigureAwait(false);
                    }
                }
                int added = messages.Count;
                DateTime lastMessage = last;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Conversations + " SET message_count = message_count + @added, last_message_utc = @last, last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @id;", command =>
                {
                    AdoDatabaseDriver.Add(command, "added", added);
                    AdoDatabaseDriver.Add(command, "last", lastMessage);
                    AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "id", conversationId);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ConversationMessage?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            ConversationMessage? message = await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, Map, token).ConfigureAwait(false);
            if (message == null) return null;
            await AttachCallsAsync(tenantId, message.ConversationId, new List<ConversationMessage> { message }, token).ConfigureAwait(false);
            return message;
        }

        /// <inheritdoc />
        public async Task<List<ConversationMessage>> ListAsync(string tenantId, string conversationId, bool activeOnly, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));
            string filter = activeOnly ? " AND compacted = " + _Driver.Dialect.BooleanLiteral(false) : String.Empty;
            List<ConversationMessage> messages = await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND conversation_id = @conversation_id" + filter + " ORDER BY sequence ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "conversation_id", conversationId);
            }, Map, token).ConfigureAwait(false);
            await AttachCallsAsync(tenantId, conversationId, messages, token).ConfigureAwait(false);
            return messages;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ConversationMessage>> EnumerateAsync(string tenantId, string conversationId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "conversation_id", conversationId);
            };
            string where = " WHERE tenant_id = @tenant_id AND conversation_id = @conversation_id";
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<ConversationMessage> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + " ORDER BY sequence ASC" + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            await AttachCallsAsync(tenantId, conversationId, objects, token).ConfigureAwait(false);
            return EnumerationResult<ConversationMessage>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<int> MarkCompactedAsync(string tenantId, string conversationId, List<string> messageIds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));
            if (messageIds == null) throw new ArgumentNullException(nameof(messageIds));
            int changed = 0;
            foreach (List<string> chunk in messageIds.Distinct(StringComparer.Ordinal).Chunk(200).Select(c => c.ToList()))
            {
                List<string> names = new List<string>();
                for (int i = 0; i < chunk.Count; i++) names.Add("@m" + i);
                changed += await _Driver.NonQueryAsync("UPDATE " + _Table + " SET compacted = " + _Driver.Dialect.BooleanLiteral(true) + " WHERE tenant_id = @tenant_id AND conversation_id = @conversation_id AND id IN (" + String.Join(", ", names) + ");", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "conversation_id", conversationId);
                    for (int i = 0; i < chunk.Count; i++) AdoDatabaseDriver.Add(command, "m" + i, chunk[i]);
                }, token).ConfigureAwait(false);
            }
            return changed;
        }

        /// <inheritdoc />
        public async Task UpdateTurnAsync(string tenantId, string id, long? timeToFirstTokenMs, long durationMs, string finishReason, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET time_to_first_token_ms = @ttft, duration_ms = @duration, finish_reason = @finish WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "ttft", timeToFirstTokenMs);
                AdoDatabaseDriver.Add(command, "duration", Math.Max(0, durationMs));
                AdoDatabaseDriver.Add(command, "finish", finishReason ?? "completed");
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a message (without tool calls).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Message.</returns>
        public static ConversationMessage Map(DbDataReader reader)
        {
            ConversationMessage obj = new ConversationMessage();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.FeedbackRating = AdoConverters.NullableStr(reader, "feedback_rating");
            obj.FeedbackComment = AdoConverters.NullableStr(reader, "feedback_comment");
            obj.FeedbackUpdatedUtc = AdoConverters.NullableDate(reader, "feedback_updated_utc");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.ConversationId = AdoConverters.Str(reader, "conversation_id");
            obj.TurnId = AdoConverters.NullableStr(reader, "turn_id");
            obj.Sequence = AdoConverters.Long(reader, "sequence");
            obj.Role = AdoConverters.Enum(reader, "role", AgentMessageRoleEnum.User);
            obj.Content = AdoConverters.Str(reader, "content");
            obj.ThinkingContent = AdoConverters.NullableStr(reader, "thinking_content");
            obj.ToolCallId = AdoConverters.NullableStr(reader, "tool_call_id");
            obj.ToolName = AdoConverters.NullableStr(reader, "tool_name");
            obj.ModelEndpointId = AdoConverters.NullableStr(reader, "model_endpoint_id");
            obj.InputTokens = AdoConverters.Long(reader, "input_tokens");
            obj.OutputTokens = AdoConverters.Long(reader, "output_tokens");
            obj.TimeToFirstTokenMs = AdoConverters.NullableLong(reader, "time_to_first_token_ms");
            obj.DurationMs = AdoConverters.NullableLong(reader, "duration_ms");
            obj.FinishReason = AdoConverters.NullableStr(reader, "finish_reason");
            obj.Compacted = AdoConverters.Bool(reader, "compacted");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private async Task AttachCallsAsync(string tenantId, string conversationId, List<ConversationMessage> messages, CancellationToken token)
        {
            if (messages.Count == 0 || !messages.Any(m => m.Role == AgentMessageRoleEnum.Assistant)) return;
            Dictionary<string, ConversationMessage> byId = messages.ToDictionary(m => m.Id, StringComparer.Ordinal);
            List<KeyValuePair<string, ConversationToolCall>> rows = await _Driver.QueryAsync("SELECT message_id, call_id, name, arguments_json FROM " + _Calls + " WHERE tenant_id = @tenant_id AND conversation_id = @conversation_id ORDER BY message_id ASC, ordinal ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "conversation_id", conversationId);
            }, reader =>
            {
                ConversationToolCall call = new ConversationToolCall();
                call.CallId = AdoConverters.Str(reader, "call_id");
                call.Name = AdoConverters.Str(reader, "name");
                call.ArgumentsJson = AdoConverters.Str(reader, "arguments_json");
                return new KeyValuePair<string, ConversationToolCall>(AdoConverters.Str(reader, "message_id"), call);
            }, token).ConfigureAwait(false);
            foreach (KeyValuePair<string, ConversationToolCall> row in rows)
            {
                if (byId.TryGetValue(row.Key, out ConversationMessage? message)) message.ToolCalls.Add(row.Value);
            }
        }

        private static void Bind(DbCommand command, ConversationMessage obj)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "conversation_id", obj.ConversationId);
            AdoDatabaseDriver.Add(command, "turn_id", obj.TurnId);
            AdoDatabaseDriver.Add(command, "sequence", obj.Sequence);
            AdoDatabaseDriver.Add(command, "role", obj.Role.ToString());
            AdoDatabaseDriver.Add(command, "content", obj.Content);
            AdoDatabaseDriver.Add(command, "thinking_content", obj.ThinkingContent);
            AdoDatabaseDriver.Add(command, "tool_call_id", obj.ToolCallId);
            AdoDatabaseDriver.Add(command, "tool_name", obj.ToolName);
            AdoDatabaseDriver.Add(command, "model_endpoint_id", obj.ModelEndpointId);
            AdoDatabaseDriver.Add(command, "input_tokens", obj.InputTokens);
            AdoDatabaseDriver.Add(command, "output_tokens", obj.OutputTokens);
            AdoDatabaseDriver.Add(command, "time_to_first_token_ms", obj.TimeToFirstTokenMs);
            AdoDatabaseDriver.Add(command, "duration_ms", obj.DurationMs);
            AdoDatabaseDriver.Add(command, "finish_reason", obj.FinishReason);
            AdoDatabaseDriver.Add(command, "compacted", obj.Compacted);
            AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
        }

        #endregion
    }
}
