namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Approval routes (P3.16, plan section 11.4): list, read (with the tool invocation and its result once executed),
    /// approve, reject (Update on Approval), and cancel (the requester, or Update on Approval). Approving a call whose
    /// requester is not waiting runs it before the response returns.
    /// </summary>
    public class ApprovalRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/approvals";
        private readonly ServerContext _Server;
        private readonly ApprovalService _Approvals;
        private RouteRegistrar? _Routes = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="approvals">Approval service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ApprovalRoutes(ServerContext server, ApprovalService approvals)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            _Routes = routes ?? throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Approval, OperationTypeEnum.Read, ListAsync, "List approvals (filters: status, agentProfileId, resourceId, principalId, toolName)", "Approvals", null, typeof(EnumerationResult<Approval>));
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.Approval, OperationTypeEnum.Read, ReadAsync, "Read an approval with its tool invocation (and result once executed)", "Approvals", null, typeof(Approval));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/approve", ResourceTypeEnum.Approval, OperationTypeEnum.Update, ApproveAsync, "Approve a pending call; when nobody is waiting, the call runs before the response returns", "Approvals", typeof(ApprovalDecisionRequest), typeof(Approval));
            routes.Scoped(HttpMethod.POST, _Base + "/{id}/reject", ResourceTypeEnum.Approval, OperationTypeEnum.Update, RejectAsync, "Reject a pending call", "Approvals", typeof(ApprovalDecisionRequest), typeof(Approval));
            routes.Authenticated(HttpMethod.POST, _Base + "/{id}/cancel", CancelAsync, "Cancel a pending call (the requester, or Update on Approval)", "Approvals", typeof(ApprovalDecisionRequest), typeof(Approval));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            ToolInvocationFilter filter = new ToolInvocationFilter();
            filter.ApprovalStatus = RouteHelpers.QueryEnum<ApprovalStatusEnum>(http, "status");
            filter.AgentProfileId = RouteHelpers.Query(http, "agentProfileId");
            filter.ResourceId = RouteHelpers.Query(http, "resourceId");
            filter.PrincipalId = RouteHelpers.Query(http, "principalId");
            filter.ToolName = RouteHelpers.Query(http, "toolName");
            EnumerationResult<Approval> page = await _Server.Database.Approvals.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), filter, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            Approval approval = await _Approvals.ReadAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, await WithInvocationAsync(approval, http).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ApproveAsync(HttpContextBase http)
        {
            await DecideAsync(http, ApprovalStatusEnum.Approved).ConfigureAwait(false);
        }

        private async Task RejectAsync(HttpContextBase http)
        {
            await DecideAsync(http, ApprovalStatusEnum.Rejected).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase http)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            string id = RouteHelpers.Param(http, "id")!;
            Approval approval = await _Approvals.ReadAsync(tenantId, id, http.Token).ConfigureAwait(false);
            bool requester = !String.IsNullOrEmpty(approval.RequestedByPrincipalId) && String.Equals(approval.RequestedByPrincipalId, ctx.PrincipalId, StringComparison.Ordinal);
            if (!requester && !await _Routes!.RequireAsync(http, tenantId, ResourceTypeEnum.Approval, OperationTypeEnum.Update, id).ConfigureAwait(false)) return;
            await DecideAsync(http, ApprovalStatusEnum.Cancelled).ConfigureAwait(false);
        }

        private async Task DecideAsync(HttpContextBase http, ApprovalStatusEnum status)
        {
            ApprovalDecisionRequest request = RouteHelpers.Body<ApprovalDecisionRequest>(http) ?? new ApprovalDecisionRequest();
            Approval decided = await _Approvals.DecideAsync(RouteHelpers.Context(http), RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, status, request.Note, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, await WithInvocationAsync(decided, http).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task<Approval> WithInvocationAsync(Approval approval, HttpContextBase http)
        {
            approval.Invocation = await _Server.Database.ToolInvocations.ReadAsync(approval.TenantId, approval.ToolInvocationId, http.Token).ConfigureAwait(false);
            return approval;
        }

        #endregion
    }
}
