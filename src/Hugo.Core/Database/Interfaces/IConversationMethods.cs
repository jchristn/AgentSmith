namespace Hugo.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;

    /// <summary>
    /// Conversation data access. Every method is tenant scoped. Thread-safe.
    /// </summary>
    public interface IConversationMethods
    {
        /// <summary>
        /// Create a conversation.
        /// </summary>
        /// <param name="obj">Conversation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<Conversation> CreateAsync(Conversation obj, CancellationToken token = default);

        /// <summary>
        /// Read a conversation.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<Conversation?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read the conversation bound to an A2A context of a profile.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="agentProfileId">Agent profile identifier.</param>
        /// <param name="contextId">A2A context identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        Task<Conversation?> ReadByA2AContextAsync(string tenantId, string agentProfileId, string contextId, CancellationToken token = default);

        /// <summary>
        /// Read the conversation a principal keeps under a session key (OpenAI-compatible <c>user</c> field).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="agentProfileId">Agent profile identifier.</param>
        /// <param name="userId">Owning user, or null.</param>
        /// <param name="credentialId">Credential, or null.</param>
        /// <param name="externalKey">Session key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or empty.</exception>
        Task<Conversation?> ReadByExternalKeyAsync(string tenantId, string agentProfileId, string? userId, string? credentialId, string externalKey, CancellationToken token = default);

        /// <summary>
        /// Update the mutable fields (title, counters).
        /// </summary>
        /// <param name="obj">Conversation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The conversation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        Task<Conversation> UpdateAsync(Conversation obj, CancellationToken token = default);

        /// <summary>
        /// Add a turn's usage to the conversation's counters atomically.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="inputTokens">Input tokens to add.</param>
        /// <param name="outputTokens">Output tokens to add.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task AddUsageAsync(string tenantId, string id, long inputTokens, long outputTokens, CancellationToken token = default);

        /// <summary>
        /// Delete a conversation with its messages, tool calls, A2A tasks, and task artifacts, in one transaction.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a conversation was deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an identifier is null or empty.</exception>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate conversations, most recently active first unless the query orders otherwise.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="query">Paging, ordering, and title search.</param>
        /// <param name="filter">Filters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or query is null.</exception>
        Task<EnumerationResult<Conversation>> EnumerateAsync(string tenantId, EnumerationQuery query, ConversationFilter? filter = null, CancellationToken token = default);
    }
}
