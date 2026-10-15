namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Server.Protocols.OpenAI;

    /// <summary>
    /// Writes a turn's answer text as OpenAI <c>chat.completion.chunk</c> events: a first chunk with the assistant role,
    /// then one chunk per <c>assistant.delta</c>. Tool activity, reasoning, and approval cards are not part of the
    /// OpenAI stream; a pending approval arrives as answer text. Thread-safe.
    /// </summary>
    public class OpenAIStreamSink : IAgentEventSink
    {
        #region Private-Members

        private readonly SseWriter _Writer;
        private readonly string _Id;
        private readonly string _Model;
        private readonly long _Created;
        private readonly JsonSerializerOptions _Options;
        private int _RoleSent = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="writer">SSE writer.</param>
        /// <param name="id">Completion identifier.</param>
        /// <param name="model">Model (profile name).</param>
        /// <param name="created">Creation time (Unix seconds).</param>
        /// <param name="options">Serializer options.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public OpenAIStreamSink(SseWriter writer, string id, string model, long created, JsonSerializerOptions options)
        {
            _Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _Id = id ?? throw new ArgumentNullException(nameof(id));
            _Model = model ?? throw new ArgumentNullException(nameof(model));
            _Created = created;
            _Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Send the role chunk if it has not been sent yet.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureRoleAsync(CancellationToken token)
        {
            if (Interlocked.Exchange(ref _RoleSent, 1) == 1) return;
            await SendAsync(new OpenAIChatMessage { Role = "assistant", Content = String.Empty }, null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent is AssistantDeltaEvent delta && !String.IsNullOrEmpty(delta.Text))
            {
                await EnsureRoleAsync(CancellationToken.None).ConfigureAwait(false);
                await SendAsync(new OpenAIChatMessage { Content = delta.Text }, null, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Build a chunk.
        /// </summary>
        /// <param name="delta">Delta, or null.</param>
        /// <param name="finishReason">Finish reason, or null.</param>
        /// <returns>Chunk.</returns>
        public OpenAIChatResponse Chunk(OpenAIChatMessage? delta, string? finishReason)
        {
            OpenAIChatResponse chunk = new OpenAIChatResponse();
            chunk.Id = _Id;
            chunk.Object = "chat.completion.chunk";
            chunk.Created = _Created;
            chunk.Model = _Model;
            chunk.Choices = new List<OpenAIChoice> { new OpenAIChoice { Index = 0, Delta = delta ?? new OpenAIChatMessage(), FinishReason = finishReason } };
            return chunk;
        }

        #endregion

        #region Private-Methods

        private async Task SendAsync(OpenAIChatMessage delta, string? finishReason, CancellationToken token)
        {
            await _Writer.SendAsync(null, JsonSerializer.Serialize(Chunk(delta, finishReason), _Options), null, token).ConfigureAwait(false);
        }

        #endregion
    }
}
