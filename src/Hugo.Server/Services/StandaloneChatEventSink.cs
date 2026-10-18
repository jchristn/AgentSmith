namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;

    /// <summary>
    /// Emits an explicit allowlist of visitor-safe streaming DTOs. Internal events are never serialized.
    /// Holds incomplete secrets across model chunks and revalidates publication before every text emission.
    /// Used by one sequential turn event publisher.
    /// </summary>
    public class StandaloneChatEventSink : IAgentEventSink
    {
        private readonly SseWriter _Writer;
        private readonly StandaloneChatService _Chat;
        private readonly StandaloneChatAccess _Access;
        private readonly string _ConversationId;
        private string _Pending = String.Empty;

        /// <summary>Construct a visitor-safe event projection.</summary>
        /// <param name="writer">SSE writer.</param>
        /// <param name="chat">Access validation service.</param>
        /// <param name="access">Original access.</param>
        /// <param name="conversationId">Visitor conversation.</param>
        public StandaloneChatEventSink(SseWriter writer, StandaloneChatService chat, StandaloneChatAccess access, string conversationId)
        {
            _Writer = writer;
            _Chat = chat;
            _Access = access;
            _ConversationId = conversationId;
        }

        /// <inheritdoc />
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent is TurnStartedEvent)
            {
                await _Chat.RevalidateAsync(_Access, token).ConfigureAwait(false);
                _Writer.Start();
                await _Writer.SendAsync("turn.started", Serializer.Serialize(new { conversationId = _ConversationId }), null, token).ConfigureAwait(false);
            }
            else if (agentEvent is AssistantDeltaEvent delta)
            {
                _Pending += delta.Text;
                int length = SecretScrubber.Shared.StreamingPrefixLength(_Pending);
                if (length == 0) return;
                await _Chat.RevalidateAsync(_Access, token).ConfigureAwait(false);
                string text = SecretScrubber.Shared.Scrub(_Pending.Substring(0, length)) ?? String.Empty;
                _Pending = _Pending.Substring(length);
                // A disconnected viewer does not cancel the accepted turn. Authority checks above remain active.
                await _Writer.SendAsync("assistant.delta", Serializer.Serialize(new { text }), null, token).ConfigureAwait(false);
            }
            // Completion is projected by the route only after final authority checks and persistence.
        }
    }
}
