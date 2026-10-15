namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;

    /// <summary>
    /// Publishes a turn's lifecycle events to the tenant event hub (<c>GET /v1.0/api/events</c>) as
    /// <c>conversation.&lt;event name&gt;</c>, so other dashboard tabs see turns, tool calls, and approvals as they happen.
    /// Streaming text (assistant.delta, assistant.thinking) is not published. Thread-safe.
    /// </summary>
    public class HubAgentEventSink : IAgentEventSink
    {
        #region Private-Members

        private readonly EventHub _Hub;
        private readonly string _TenantId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="hub">Event hub.</param>
        /// <param name="tenantId">Tenant the events belong to.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public HubAgentEventSink(EventHub hub, string tenantId)
        {
            _Hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task PublishAsync(AgentEvent agentEvent, CancellationToken token)
        {
            if (agentEvent == null || agentEvent is AssistantDeltaEvent || agentEvent is AssistantThinkingEvent) return Task.CompletedTask;
            _Hub.Publish("conversation." + agentEvent.EventName, _TenantId, agentEvent);
            return Task.CompletedTask;
        }

        #endregion
    }
}
