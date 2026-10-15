namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Conversations and chat (/v1.0/api/conversations), including streamed turns as ChatEvent async enumerables.
    /// Sending a message runs an agent turn with the conversation's agent profile, which needs an inference endpoint.
    /// </summary>
    public class ConversationsClient : CrudClientBase<Conversation, ConversationRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ConversationsClient(HugoTransport transport) : base(transport, "conversations", false)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List one page of the caller's conversations (tenant administrators see all) with filters.
        /// </summary>
        /// <param name="filter">Filter (agent profile, source).</param>
        /// <param name="query">Optional paging, ordering, and search.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (400 ValidationFailed for an unknown source).</exception>
        public Task<EnumerationResult<Conversation>> ListAsync(ConversationFilter filter, EnumerationQuery? query = null, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            Dictionary<string, string?> filters = new Dictionary<string, string?>
            {
                { "agentProfileId", filter.AgentProfileId },
                { "source", filter.Source }
            };
            return ListAsync(query, filters, token);
        }

        /// <summary>
        /// List one page of a conversation's messages in order (compacted ones included, with their tool calls).
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="query">Optional paging; without one the server returns up to 1000 messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of messages.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task<EnumerationResult<ConversationMessage>> ListMessagesAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            Dictionary<string, string?>? parameters = query == null ? null : QueryBuilder.From(query);
            EnumerationResult<ConversationMessage>? page = await Transport.SendAsync<EnumerationResult<ConversationMessage>>(HttpMethod.Get, Item(id) + "/messages", null, parameters, token).ConfigureAwait(false);
            return page ?? new EnumerationResult<ConversationMessage>();
        }

        /// <summary>
        /// Send a user message, run the turn, and wait for its result (JSON). Approvals raised during the turn are
        /// waited on; the result reports ApprovalPending when they time out.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="message">Message text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Turn result with the final text, messages, tool calls, and token counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (409 TurnInProgress when a turn is already running).</exception>
        public Task<AgentTurnResult> SendMessageAsync(string id, string message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));
            ChatMessageRequest request = new ChatMessageRequest();
            request.Message = message;
            return PostAsync<AgentTurnResult>(Item(id) + "/messages", request, token);
        }

        /// <summary>
        /// Send a user message and stream the turn as chat events (turn.started, assistant.delta, assistant.thinking,
        /// tool.*, approval.*, turn.completed or error, then stream.closed). The enumeration ends after stream.closed.
        /// Breaking out of the loop or cancelling the token closes the stream; use CancelAsync to stop the turn on the
        /// server.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="message">Message text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chat events.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(string id, string message, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));
            ChatMessageRequest request = new ChatMessageRequest();
            request.Message = message;
            await foreach (SseEvent item in Transport.StreamAsync(HttpMethod.Post, Item(id) + "/messages", request, null, token).ConfigureAwait(false))
            {
                ChatEvent chat = ChatEvent.FromSse(item);
                yield return chat;
                if (chat.Type == ChatEventTypes.StreamClosed) yield break;
            }
        }

        /// <summary>
        /// Cancel the running turn of a conversation.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Whether a running turn was cancelled.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ConversationCancelResponse> CancelAsync(string id, CancellationToken token = default)
        {
            return PostAsync<ConversationCancelResponse>(Item(id) + "/cancel", null, token);
        }

        /// <summary>
        /// Export a conversation.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="format">md (Markdown) or json. Default md.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exported text.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status (400 for another format).</exception>
        public Task<string> ExportAsync(string id, string format = "md", CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "format", format } };
            return Transport.SendTextAsync(HttpMethod.Get, Item(id) + "/export", null, query, null, true, token);
        }

        #endregion
    }
}
