namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;

    /// <summary>
    /// Conversation message data access, with tool calls as a child table. Every method is tenant scoped. Thread-safe;
    /// callers serialize appends per conversation (the runtime's conversation lock).
    /// </summary>
    public interface IMessageMethods
    {
        /// <summary>Page submitted feedback newest first, restricted by tenant, profile and optional conversation owner. Cleared feedback is excluded.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="agentProfileId">Profile identifier.</param>
        /// <param name="userId">Restrict conversation owner, or null for no user filter.</param>
        /// <param name="credentialId">Restrict owning credential, or null for no credential filter.</param>
        /// <param name="messageId">Restrict rated message for a detail read, or null.</param>
        /// <param name="query">Paging; sorting is always latest feedback first.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Feedback page with associated persisted telemetry.</returns>
        Task<EnumerationResult<FeedbackReview>> EnumerateFeedbackAsync(string tenantId, string agentProfileId, string? userId, string? credentialId, string? messageId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>Update an assistant message's visitor feedback in a tenant and conversation.</summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="conversationId">Conversation.</param>
        /// <param name="id">Assistant message.</param>
        /// <param name="rating">Positive, Negative, or null.</param>
        /// <param name="comment">Optional comment.</param>
        /// <param name="token">Cancellation.</param>
        /// <returns>Whether a matching assistant message was updated.</returns>
        Task<bool> UpdateFeedbackAsync(string tenantId, string conversationId, string id, string? rating, string? comment, CancellationToken token = default);

        /// <summary>
        /// Append messages in order, assigning sequence numbers after the conversation's last message, and update the
        /// conversation's message count and last message time, in one transaction.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="messages">Messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task AppendAsync(string tenantId, string conversationId, List<ConversationMessage> messages, CancellationToken token = default);

        /// <summary>
        /// Read one message with its tool calls.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<ConversationMessage?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// List a conversation's messages in sequence order with their tool calls.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="activeOnly">True to leave out compacted messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<List<ConversationMessage>> ListAsync(string tenantId, string conversationId, bool activeOnly, CancellationToken token = default);

        /// <summary>
        /// Page through a conversation's messages in sequence order (oldest first), compacted ones included.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="query">Paging (ordering is always by sequence).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task<EnumerationResult<ConversationMessage>> EnumerateAsync(string tenantId, string conversationId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Mark messages compacted (replaced by a summary in the active history).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="messageIds">Message identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        Task<int> MarkCompactedAsync(string tenantId, string conversationId, List<string> messageIds, CancellationToken token = default);

        /// <summary>
        /// Record a turn's timing and finish reason on its final message.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Message identifier.</param>
        /// <param name="timeToFirstTokenMs">Time to first token, or null.</param>
        /// <param name="durationMs">Turn duration.</param>
        /// <param name="finishReason">Finish reason label.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task UpdateTurnAsync(string tenantId, string id, long? timeToFirstTokenMs, long durationMs, string finishReason, CancellationToken token = default);
    }
}
