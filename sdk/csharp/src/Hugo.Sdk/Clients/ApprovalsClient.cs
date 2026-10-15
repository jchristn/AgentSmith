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

    /// <summary>
    /// Approvals of tool calls (/v1.0/api/approvals): list, read, approve, reject, cancel, and await a decision.
    /// </summary>
    public class ApprovalsClient : ClientBase
    {
        #region Public-Members

        /// <summary>
        /// Default polling interval for AwaitDecisionAsync. Minimum 100 milliseconds, maximum 60 seconds, default 2 seconds.
        /// </summary>
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(100)) value = TimeSpan.FromMilliseconds(100);
                if (value > TimeSpan.FromSeconds(60)) value = TimeSpan.FromSeconds(60);
                _PollInterval = value;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _PollInterval = TimeSpan.FromSeconds(2);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ApprovalsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List approvals.
        /// </summary>
        /// <param name="filter">Optional filter.</param>
        /// <param name="query">Optional paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<EnumerationResult<Approval>> ListAsync(ApprovalFilter? filter = null, EnumerationQuery? query = null, CancellationToken token = default)
        {
            Dictionary<string, string?> parameters = QueryBuilder.From(query);
            if (filter != null)
            {
                parameters["status"] = filter.Status;
                parameters["agentProfileId"] = filter.AgentProfileId;
                parameters["resourceId"] = filter.ResourceId;
                parameters["principalId"] = filter.PrincipalId;
                parameters["toolName"] = filter.ToolName;
            }
            return GetAsync<EnumerationResult<Approval>>("/v1.0/api/approvals", parameters, token);
        }

        /// <summary>
        /// Read an approval with its tool invocation (and result once executed).
        /// </summary>
        /// <param name="id">Approval identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Approval> GetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Approval>(Path(id), null, token);
        }

        /// <summary>
        /// Approve a pending call.
        /// </summary>
        /// <param name="id">Approval identifier.</param>
        /// <param name="note">Optional decision note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Approval> ApproveAsync(string id, string? note = null, CancellationToken token = default)
        {
            return DecideAsync(id, "approve", note, token);
        }

        /// <summary>
        /// Reject a pending call.
        /// </summary>
        /// <param name="id">Approval identifier.</param>
        /// <param name="note">Optional decision note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Approval> RejectAsync(string id, string? note = null, CancellationToken token = default)
        {
            return DecideAsync(id, "reject", note, token);
        }

        /// <summary>
        /// Cancel a pending call (the requester, or Update on Approval).
        /// </summary>
        /// <param name="id">Approval identifier.</param>
        /// <param name="note">Optional note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Approval> CancelAsync(string id, string? note = null, CancellationToken token = default)
        {
            return DecideAsync(id, "cancel", note, token);
        }

        /// <summary>
        /// Poll an approval until it leaves the Pending status or the timeout elapses.
        /// </summary>
        /// <param name="id">Approval identifier.</param>
        /// <param name="timeout">Maximum wait; null waits until the token is cancelled.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The approval in its final status, or still Pending when the timeout elapsed.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<Approval> AwaitDecisionAsync(string id, TimeSpan? timeout = null, CancellationToken token = default)
        {
            DateTime? deadline = timeout.HasValue ? DateTime.UtcNow + timeout.Value : null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Approval approval = await GetAsync(id, token).ConfigureAwait(false);
                if (!String.Equals(approval.Status, "Pending", StringComparison.OrdinalIgnoreCase)) return approval;
                if (deadline.HasValue && DateTime.UtcNow >= deadline.Value) return approval;
                await Task.Delay(_PollInterval, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static string Path(string id)
        {
            return "/v1.0/api/approvals/" + Segment(id);
        }

        private Task<Approval> DecideAsync(string id, string action, string? note, CancellationToken token)
        {
            ApprovalDecisionRequest request = new ApprovalDecisionRequest();
            request.Note = note;
            return PostAsync<Approval>(Path(id) + "/" + action, request, token);
        }

        #endregion
    }
}
