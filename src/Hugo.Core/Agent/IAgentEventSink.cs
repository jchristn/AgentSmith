namespace Hugo.Core.Agent
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// Receives the typed events of a turn (D-022) in sequence order, one at a time. The server implements it to
    /// write SSE frames, publish to the event hub, or map to A2A task updates. A sink that throws does not break
    /// the turn; the runtime logs nothing and carries on.
    /// </summary>
    public interface IAgentEventSink
    {
        /// <summary>
        /// Publish one event.
        /// </summary>
        /// <param name="agentEvent">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task PublishAsync(AgentEvent agentEvent, CancellationToken token);
    }
}
