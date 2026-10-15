namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// Records events and optionally reacts to them (for example to approve or cancel mid-turn). Thread-safe.
    /// </summary>
    public class RecordingEventSink : IAgentEventSink
    {
        #region Public-Members

        /// <summary>
        /// Called for each event after it is recorded. Null for none.
        /// </summary>
        public Action<AgentEvent>? OnEvent { get; set; } = null;

        /// <summary>
        /// Events in delivery order.
        /// </summary>
        public List<AgentEvent> Events
        {
            get
            {
                lock (_Lock) return _Events.ToList();
            }
        }

        /// <summary>
        /// Event names in delivery order.
        /// </summary>
        public List<string> Names
        {
            get
            {
                return Events.Select(e => e.EventName).ToList();
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<AgentEvent> _Events = new List<AgentEvent>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            lock (_Lock) _Events.Add(agentEvent);
            OnEvent?.Invoke(agentEvent);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Events of a type.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <returns>Events.</returns>
        public List<T> Of<T>() where T : AgentEvent
        {
            return Events.OfType<T>().ToList();
        }

        #endregion
    }
}
