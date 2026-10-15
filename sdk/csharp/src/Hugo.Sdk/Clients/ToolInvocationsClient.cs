namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Tool invocations: history, summary, dry run, and direct invoke as a profile (/v1.0/api/tool-invocations).
    /// </summary>
    public class ToolInvocationsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ToolInvocationsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List recorded tool invocations.
        /// </summary>
        /// <param name="filter">Optional filter.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<ToolInvocation>> ListAsync(ToolInvocationFilter? filter = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            Dictionary<string, string?> parameters = QueryBuilder.From(query);
            if (filter != null)
            {
                parameters["agentProfileId"] = filter.AgentProfileId;
                parameters["resourceId"] = filter.ResourceId;
                parameters["toolName"] = filter.ToolName;
                parameters["decision"] = filter.Decision;
                parameters["status"] = filter.Status;
                parameters["surface"] = filter.Surface;
                parameters["conversationId"] = filter.ConversationId;
                parameters["principalId"] = filter.PrincipalId;
                parameters["since"] = QueryBuilder.Date(filter.Since);
            }
            return GetAsync<EnumerationResult<ToolInvocation>>("/v1.0/api/tool-invocations", parameters, token);
        }

        /// <summary>
        /// Read a tool invocation.
        /// </summary>
        /// <param name="id">Invocation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Invocation.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ToolInvocation> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ToolInvocation>("/v1.0/api/tool-invocations/" + Segment(id), null, token);
        }

        /// <summary>
        /// Counts by decision, status, effect, and tool.
        /// </summary>
        /// <param name="since">Optional start, UTC.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ToolInvocationSummary> GetSummaryAsync(DateTime? since = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?> { { "since", QueryBuilder.Date(since) } };
            return GetAsync<ToolInvocationSummary>("/v1.0/api/tool-invocations/summary", query, token);
        }

        /// <summary>
        /// Run resolution, authorization, validation, classification, and rules only, and return the decision.
        /// Nothing is recorded or executed.
        /// </summary>
        /// <param name="request">Tool call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome with the decision and classification.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ToolInvocationOutcome> DryRunAsync(ToolCallRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<ToolInvocationOutcome>("/v1.0/api/tool-invocations/dry-run", request, token);
        }

        /// <summary>
        /// Invoke a tool as a profile through the full pipeline. While approval is pending the server answers 202
        /// and the outcome carries ApprovalId; use ApprovalsClient.AwaitDecisionAsync to wait for it.
        /// </summary>
        /// <param name="request">Tool call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<ToolInvocationOutcome> InvokeAsync(ToolCallRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return PostAsync<ToolInvocationOutcome>("/v1.0/api/tool-invocations", request, token);
        }

        #endregion
    }
}
