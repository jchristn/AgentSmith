namespace Hugo.Server.Services
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Serialization;

    /// <summary>
    /// Writes each agent event as a named SSE event (D-022): the event name is the event's <c>EventName</c>
    /// (turn.started, assistant.delta, ...), the data is the event as camelCase JSON, and the event identifier is its
    /// sequence number. Thread-safe.
    /// </summary>
    public class SseAgentEventSink : IAgentEventSink
    {
        #region Private-Members

        private readonly SseWriter _Writer;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="writer">SSE writer.</param>
        /// <exception cref="ArgumentNullException">Thrown when writer is null.</exception>
        public SseAgentEventSink(SseWriter writer)
        {
            _Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent == null) return;
            await _Writer.SendAsync(agentEvent.EventName, Serializer.Serialize(agentEvent), agentEvent.Sequence.ToString(CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
        }

        #endregion
    }
}
