namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Conversation persistence used by the runtime. The server implements it over the <c>conversations</c> and
    /// <c>messages</c> tables (P6.1, P6.2); <see cref="InMemoryConversationStore"/> serves tests and benchmarks.
    /// Tenant scoping is the implementation's job: it is constructed for, or checks, the caller's tenant.
    /// Must be thread-safe.
    /// </summary>
    public interface IConversationStore
    {
        /// <summary>
        /// Load the active history, oldest first: a compaction summary (if any) followed by the messages after it.
        /// Compacted messages are excluded.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages. Never null; empty for a new conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when conversationId is null.</exception>
        Task<List<AgentMessage>> LoadHistoryAsync(string conversationId, CancellationToken token);

        /// <summary>
        /// Append messages in order.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <param name="messages">Messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task AppendMessagesAsync(string conversationId, List<AgentMessage> messages, CancellationToken token);

        /// <summary>
        /// Persist a compaction: store the summary and mark the covered messages compacted, atomically.
        /// </summary>
        /// <param name="conversationId">Conversation ID.</param>
        /// <param name="compaction">Compaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task SaveCompactionAsync(string conversationId, AgentCompaction compaction, CancellationToken token);
    }
}
