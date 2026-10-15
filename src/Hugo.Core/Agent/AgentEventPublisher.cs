namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// Stamps and delivers one turn's events to its <see cref="IAgentEventSink"/> one at a time, in sequence order,
    /// even when parallel tool calls publish concurrently. Sink failures are swallowed so a broken client stream
    /// never breaks the turn. Keeps a copy of every event for the turn result and tests. Thread-safe.
    /// </summary>
    public class AgentEventPublisher
    {
        #region Public-Members

        /// <summary>
        /// Events published so far, in order.
        /// </summary>
        public List<AgentEvent> Published
        {
            get
            {
                lock (_Events) return new List<AgentEvent>(_Events);
            }
        }

        #endregion

        #region Private-Members

        private readonly IAgentEventSink? _Sink;
        private readonly string _TurnId;
        private readonly string _ConversationId;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly List<AgentEvent> _Events = new List<AgentEvent>();
        private long _Sequence = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sink">Sink, or null to only record.</param>
        /// <param name="turnId">Turn ID.</param>
        /// <param name="conversationId">Conversation ID.</param>
        public AgentEventPublisher(IAgentEventSink? sink, string turnId, string conversationId)
        {
            _Sink = sink;
            _TurnId = turnId ?? String.Empty;
            _ConversationId = conversationId ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stamp and publish an event. Never throws except for cancellation of the wait for the publish gate,
        /// which is not observed: publishing uses <see cref="CancellationToken.None"/> for the gate so the final
        /// events of a cancelled turn still go out.
        /// </summary>
        /// <param name="agentEvent">Event.</param>
        /// <param name="token">Cancellation token passed to the sink.</param>
        /// <returns>Task.</returns>
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent == null) return;
            await _Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                agentEvent.TurnId = _TurnId;
                agentEvent.ConversationId = _ConversationId;
                agentEvent.Sequence = ++_Sequence;
                agentEvent.TimestampUtc = DateTime.UtcNow;
                lock (_Events) _Events.Add(agentEvent);
                if (_Sink != null)
                {
                    try
                    {
                        await _Sink.PublishAsync(agentEvent, token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion
    }
}
