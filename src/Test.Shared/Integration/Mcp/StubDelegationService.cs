namespace Test.Shared.Integration.Mcp
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Security;
    using Hugo.Server.Services;

    /// <summary>
    /// A delegation service that answers without a model, for the ask_agent_smith cases.
    /// </summary>
    public class StubDelegationService : IAgentDelegationService
    {
        /// <summary>
        /// The last message received.
        /// </summary>
        public string? LastMessage { get; private set; } = null;

        /// <inheritdoc />
        public Task<AgentDelegationResult> AskAsync(RequestContext principal, string tenantId, string profileId, string message, string? conversationId, CancellationToken token)
        {
            LastMessage = message;
            AgentDelegationResult result = new AgentDelegationResult();
            result.FinalText = "Mr. Anderson, the answer to '" + message + "' is 42.";
            result.ConversationId = conversationId ?? "cnv_stub";
            result.Status = AgentDelegationStatusEnum.Completed;
            result.ToolTrace = new List<string> { "echo_read on echo-ro: Succeeded" };
            return Task.FromResult(result);
        }
    }
}
