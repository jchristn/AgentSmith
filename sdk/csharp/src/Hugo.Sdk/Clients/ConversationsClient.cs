namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;

    /// <summary>
    /// Conversations and chat (/v1.0/api/conversations), including streamed turns as ChatEvent async enumerables.
    /// Preview: verified against the server when the Phase 7 conversation routes land.
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
        /// List the messages of a conversation in order.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<ConversationMessage>> ListMessagesAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<ConversationMessage>>(Item(id) + "/messages", null, token);
        }

        /// <summary>
        /// Send a user message and wait for the turn to complete (JSON result).
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="content">Message text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The assistant's reply.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ConversationMessage> SendMessageAsync(string id, string content, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(content)) throw new ArgumentNullException(nameof(content));
            SendMessageRequest request = new SendMessageRequest();
            request.Content = content;
            return PostAsync<ConversationMessage>(Item(id) + "/messages", request, token);
        }

        /// <summary>
        /// Send a user message and stream the turn as chat events (turn.started, assistant.delta, tool.*, approval.*,
        /// turn.completed, error). Breaking out of the loop or cancelling the token closes the stream; use CancelAsync
        /// to stop the turn on the server.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="content">Message text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chat events.</returns>
        /// <exception cref="ArgumentNullException">Thrown when content is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async IAsyncEnumerable<ChatEvent> StreamMessageAsync(string id, string content, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(content)) throw new ArgumentNullException(nameof(content));
            SendMessageRequest request = new SendMessageRequest();
            request.Content = content;
            await foreach (SseEvent item in Transport.StreamAsync(HttpMethod.Post, Item(id) + "/messages", request, null, token).ConfigureAwait(false))
            {
                ChatEvent chat = ChatEvent.FromSse(item);
                yield return chat;
                if (chat.IsTerminal) yield break;
            }
        }

        /// <summary>
        /// Cancel the running turn of a conversation.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public async Task CancelAsync(string id, CancellationToken token = default)
        {
            await Transport.SendTextAsync(HttpMethod.Post, Item(id) + "/cancel", null, null, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Export a conversation.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="format">md (Markdown) or json. Default md.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exported text.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<string> ExportAsync(string id, string format = "md", CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "format", format } };
            return Transport.SendTextAsync(HttpMethod.Get, Item(id) + "/export", null, query, null, true, token);
        }

        #endregion
    }
}
