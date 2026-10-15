namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// <see cref="IConversationStore"/> over the <c>conversations</c>, <c>messages</c>, and <c>message_tool_calls</c>
    /// tables (P6.1, P6.2). One instance serves one tenant; build one per turn. Tool call arguments pass through the
    /// secret scrubber before they are stored. Thread-safe.
    /// </summary>
    public class ServerConversationStore : IConversationStore
    {
        #region Public-Members

        /// <summary>
        /// Model endpoint recorded on the assistant and summary messages this store appends, or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly string _TenantId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="tenantId">Tenant every operation is scoped to.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public ServerConversationStore(DatabaseDriverBase database, string tenantId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            _TenantId = tenantId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<AgentMessage>> LoadHistoryAsync(string conversationId, CancellationToken token)
        {
            if (conversationId == null) throw new ArgumentNullException(nameof(conversationId));
            List<ConversationMessage> rows = await _Database.Messages.ListAsync(_TenantId, conversationId, true, token).ConfigureAwait(false);
            List<AgentMessage> history = new List<AgentMessage>();
            foreach (ConversationMessage row in rows.Where(r => r.Role == AgentMessageRoleEnum.Summary)) history.Add(ToAgent(row));
            foreach (ConversationMessage row in rows.Where(r => r.Role != AgentMessageRoleEnum.Summary)) history.Add(ToAgent(row));
            return history;
        }

        /// <inheritdoc />
        public async Task AppendMessagesAsync(string conversationId, List<AgentMessage> messages, CancellationToken token)
        {
            if (conversationId == null) throw new ArgumentNullException(nameof(conversationId));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (messages.Count == 0) return;
            await _Database.Messages.AppendAsync(_TenantId, conversationId, messages.Select(m => FromAgent(conversationId, m)).ToList(), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SaveCompactionAsync(string conversationId, AgentCompaction compaction, CancellationToken token)
        {
            if (conversationId == null) throw new ArgumentNullException(nameof(conversationId));
            if (compaction == null) throw new ArgumentNullException(nameof(compaction));
            await _Database.Messages.AppendAsync(_TenantId, conversationId, new List<ConversationMessage> { FromAgent(conversationId, compaction.Summary) }, token).ConfigureAwait(false);
            await _Database.Messages.MarkCompactedAsync(_TenantId, conversationId, compaction.CoveredMessageIds, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Convert a stored message to the runtime's shape.
        /// </summary>
        /// <param name="row">Stored message.</param>
        /// <returns>Runtime message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when row is null.</exception>
        public static AgentMessage ToAgent(ConversationMessage row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            AgentMessage message = new AgentMessage();
            message.Id = row.Id;
            message.ConversationId = row.ConversationId;
            message.TurnId = row.TurnId;
            message.Role = row.Role;
            message.Content = row.Content;
            message.Thinking = row.ThinkingContent;
            message.ToolCallId = row.ToolCallId;
            message.ToolName = row.ToolName;
            message.TokensIn = row.InputTokens;
            message.TokensOut = row.OutputTokens;
            message.CreatedUtc = row.CreatedUtc;
            message.ToolCalls = row.ToolCalls.Select(c => new AgentMessageToolCall { Id = c.CallId, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList();
            return message;
        }

        #endregion

        #region Private-Methods

        private ConversationMessage FromAgent(string conversationId, AgentMessage message)
        {
            ConversationMessage row = new ConversationMessage();
            if (!String.IsNullOrEmpty(message.Id)) row.Id = message.Id;
            row.TenantId = _TenantId;
            row.ConversationId = conversationId;
            row.TurnId = message.TurnId;
            row.Role = message.Role;
            row.Content = message.Content;
            row.ThinkingContent = message.Thinking;
            row.ToolCallId = message.ToolCallId;
            row.ToolName = message.ToolName;
            row.InputTokens = message.TokensIn;
            row.OutputTokens = message.TokensOut;
            row.CreatedUtc = message.CreatedUtc;
            if (message.Role == AgentMessageRoleEnum.Assistant || message.Role == AgentMessageRoleEnum.Summary) row.ModelEndpointId = ModelEndpointId;
            foreach (AgentMessageToolCall call in message.ToolCalls)
            {
                ConversationToolCall stored = new ConversationToolCall();
                stored.CallId = call.Id;
                stored.Name = call.Name;
                stored.ArgumentsJson = SecretScrubber.Shared.Scrub(call.ArgumentsJson) ?? "{}";
                row.ToolCalls.Add(stored);
            }
            return row;
        }

        #endregion
    }
}
