namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// Fans one turn's events out to several sinks in order. A failing sink does not stop the others. Thread-safe when
    /// its sinks are.
    /// </summary>
    public class CompositeAgentEventSink : IAgentEventSink
    {
        #region Private-Members

        private readonly List<IAgentEventSink> _Sinks;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sinks">Sinks; null entries are skipped.</param>
        /// <exception cref="ArgumentNullException">Thrown when sinks is null.</exception>
        public CompositeAgentEventSink(IEnumerable<IAgentEventSink?> sinks)
        {
            if (sinks == null) throw new ArgumentNullException(nameof(sinks));
            _Sinks = new List<IAgentEventSink>();
            foreach (IAgentEventSink? sink in sinks)
            {
                if (sink != null) _Sinks.Add(sink);
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            foreach (IAgentEventSink sink in _Sinks)
            {
                try
                {
                    await sink.PublishAsync(agentEvent, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                }
            }
        }

        #endregion
    }
}
