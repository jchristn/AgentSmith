namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// What a surface hands <see cref="AgentTurnHost"/> to run one turn. Authentication, authorization (Execute on the
    /// profile), and conversation resolution are the surface's job. Not thread-safe; build one per turn.
    /// </summary>
    public class AgentTurnOptions
    {
        #region Public-Members

        /// <summary>
        /// Caller; tool calls run as this principal.
        /// </summary>
        public RequestContext Principal { get; set; } = new RequestContext();

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Agent profile.
        /// </summary>
        public AgentProfile Profile { get; set; } = new AgentProfile();

        /// <summary>
        /// Conversation the turn runs in.
        /// </summary>
        public Conversation Conversation { get; set; } = new Conversation();

        /// <summary>
        /// The user's message.
        /// </summary>
        public string UserMessage { get; set; } = String.Empty;

        /// <summary>
        /// Surface (compiles instructions for it, labels tool calls and metrics).
        /// </summary>
        public InvocationSurfaceEnum Surface { get; set; } = InvocationSurfaceEnum.Chat;

        /// <summary>
        /// How tool calls that need approval wait: Wait blocks until decided (chat, A2A); WaitWithTimeout gives up after
        /// <c>Approvals.McpWaitSeconds</c> (delegation); NoWait ends the turn at once (OpenAI-compatible). A turn that
        /// ends pending tells the model the outcome at the start of the conversation's next turn.
        /// </summary>
        public ApprovalWaitModeEnum WaitMode { get; set; } = ApprovalWaitModeEnum.Wait;

        /// <summary>
        /// Extra sinks for the turn's events (SSE writer, A2A task mapper). The host adds the event hub sink.
        /// </summary>
        public List<IAgentEventSink> Sinks
        {
            get
            {
                return _Sinks;
            }
            set
            {
                _Sinks = value ?? new List<IAgentEventSink>();
            }
        }

        /// <summary>
        /// Caller context appended after the compiled instructions (OpenAI-compatible client system messages), or null.
        /// </summary>
        public string? CallerContext { get; set; } = null;

        /// <summary>
        /// Whether to generate a title after the first exchange when the conversation has none (P6.16). Default true.
        /// </summary>
        public bool GenerateTitle { get; set; } = true;

        #endregion

        #region Private-Members

        private List<IAgentEventSink> _Sinks = new List<IAgentEventSink>();

        #endregion
    }
}
