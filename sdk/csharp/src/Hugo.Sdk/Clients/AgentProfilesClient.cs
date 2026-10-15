namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Agent profiles, their bindings, compiled instructions, and caller tool view (/v1.0/api/agent-profiles).
    /// </summary>
    public class AgentProfilesClient : CrudClientBase<AgentProfile, AgentProfileRequest>
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public AgentProfilesClient(HugoTransport transport) : base(transport, "agent-profiles", false)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy a profile with its bindings under a new name.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="name">Name of the copy.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>New profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AgentProfile> DuplicateAsync(string id, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            CloneRequest request = new CloneRequest();
            request.Name = name;
            return PostAsync<AgentProfile>(Item(id) + "/duplicate", request, token);
        }

        /// <summary>
        /// Compiled instructions as a caller on a surface would receive them.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="surface">Surface: Chat, Mcp, A2A, or OpenAI; null for the server default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Compiled instructions.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<CompiledInstructions> GetCompiledInstructionsAsync(string id, string? surface = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "surface", surface } };
            return GetAsync<CompiledInstructions>(Item(id) + "/compiled-instructions", query, token);
        }

        /// <summary>
        /// Compiled instruction text only (text/plain), as a caller on a surface would receive it.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="surface">Surface: Chat, Mcp, A2A, or OpenAI; null for the server default (Chat).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instruction text.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<string> GetCompiledInstructionsTextAsync(string id, string? surface = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "surface", surface }, { "text", "true" } };
            return Transport.SendTextAsync(HttpMethod.Get, Item(id) + "/compiled-instructions", null, query, null, true, token);
        }

        /// <summary>
        /// Tools as a caller of the profile would see them.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="surface">Surface whose view to return, for example rest, mcp, or chat; null for the server default (rest).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tool catalog.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<CallerToolCatalog> GetToolsAsync(string id, string? surface = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "surface", surface } };
            return GetAsync<CallerToolCatalog>(Item(id) + "/tools", query, token);
        }

        /// <summary>
        /// Preview of the profile's A2A agent card.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Card preview.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<AgentCardPreview> GetCardAsync(string id, CancellationToken token = default)
        {
            return GetAsync<AgentCardPreview>(Item(id) + "/card", null, token);
        }

        /// <summary>
        /// List a profile's bindings.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bindings.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<Binding>> ListBindingsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<Binding>>(Item(id) + "/bindings", null, token);
        }

        /// <summary>
        /// Read a binding.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Binding.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Binding> GetBindingAsync(string id, string bindingId, CancellationToken token = default)
        {
            return GetAsync<Binding>(Item(id) + "/bindings/" + Segment(bindingId), null, token);
        }

        /// <summary>
        /// Bind a resource to a profile.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="request">Binding.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created binding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Binding> CreateBindingAsync(string id, BindingRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<Binding>(Item(id) + "/bindings", request, token);
        }

        /// <summary>
        /// Update a binding (access mode, approval policy, instruction override, limits, tool rules, deny patterns).
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="request">Changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated binding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Binding> UpdateBindingAsync(string id, string bindingId, BindingRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PutAsync<Binding>(Item(id) + "/bindings/" + Segment(bindingId), request, token);
        }

        /// <summary>
        /// Remove a binding.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task DeleteBindingAsync(string id, string bindingId, CancellationToken token = default)
        {
            return DeleteAsync(Item(id) + "/bindings/" + Segment(bindingId), null, token);
        }

        /// <summary>
        /// List a binding's tool rules.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rules.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<BindingToolRule>> GetToolRulesAsync(string id, string bindingId, CancellationToken token = default)
        {
            return GetAsync<List<BindingToolRule>>(Item(id) + "/bindings/" + Segment(bindingId) + "/tool-rules", null, token);
        }

        /// <summary>
        /// Replace a binding's tool rules.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="bindingId">Binding identifier.</param>
        /// <param name="rules">Rules (tool name and Allow, Deny, or RequireApproval).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated binding.</returns>
        /// <exception cref="ArgumentNullException">Thrown when rules is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Binding> SetToolRulesAsync(string id, string bindingId, List<BindingToolRule> rules, CancellationToken token = default)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return PutAsync<Binding>(Item(id) + "/bindings/" + Segment(bindingId) + "/tool-rules", rules, token);
        }

        /// <summary>
        /// Delegate a task to the profile's own agent loop and wait for the answer (the REST twin of the MCP
        /// ask_agent_smith tool). Approvals raised during the turn are waited on for up to Approvals.McpWaitSeconds,
        /// after which the result reports ApprovalPending with the approval identifier. Needs Execute on the profile
        /// and an inference endpoint on it.
        /// </summary>
        /// <param name="id">Profile identifier.</param>
        /// <param name="message">Task or question.</param>
        /// <param name="conversationId">Conversation to continue, or null to start a new one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Final text, conversation, status (Completed, ApprovalPending, or Failed), and a tool trace.</returns>
        /// <exception cref="ArgumentNullException">Thrown when message is null or empty.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status (409 TurnInProgress when the conversation is busy).</exception>
        public Task<AgentDelegationResult> AskAsync(string id, string message, string? conversationId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));
            ChatMessageRequest request = new ChatMessageRequest();
            request.Message = message;
            request.ConversationId = conversationId;
            return PostAsync<AgentDelegationResult>(Item(id) + "/ask", request, token);
        }

        #endregion
    }
}
