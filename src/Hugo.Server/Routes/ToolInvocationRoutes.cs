namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Tool invocation routes (P3.17): list, summary, read, dry run (pipeline steps 1 to 6, nothing recorded), and direct
    /// invoke as a profile (the same pipeline; 202 with the approval while approval is pending). Dry run and invoke
    /// authorize inside the pipeline (Execute on the profile) and resolve the profile per D-018 when none is given.
    /// </summary>
    public class ToolInvocationRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/tool-invocations";
        private readonly ServerContext _Server;
        private readonly ToolInvocationService _Invocations;
        private readonly ProfileResolver _Resolver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Server context.</param>
        /// <param name="invocations">Pipeline.</param>
        /// <param name="resolver">Profile resolver.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ToolInvocationRoutes(ServerContext server, ToolInvocationService invocations, ProfileResolver resolver)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Invocations = invocations ?? throw new ArgumentNullException(nameof(invocations));
            _Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
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
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.ToolInvocation, OperationTypeEnum.Read, ListAsync, "List tool invocations (filters: agentProfileId, resourceId, toolName, decision, status, surface, effect, conversationId, principalId, fromUtc (or since), toUtc)", "Tool Invocations", null, typeof(EnumerationResult<ToolInvocation>));
            routes.Scoped(HttpMethod.GET, _Base + "/summary", ResourceTypeEnum.ToolInvocation, OperationTypeEnum.Read, SummaryAsync, "Counts by decision, status, effect, and tool (optional since)", "Tool Invocations", null, typeof(ToolInvocationSummary));
            routes.Authenticated(HttpMethod.POST, _Base + "/dry-run", DryRunAsync, "Run resolution, authorization, validation, classification, and rules only, and return the decision (nothing is recorded or executed)", "Tool Invocations", typeof(ToolCallRequest), typeof(ToolInvocationOutcome));
            routes.Authenticated(HttpMethod.POST, _Base, InvokeAsync, "Invoke a tool as a profile through the full pipeline; 202 with the approval while approval is pending", "Tool Invocations", typeof(ToolCallRequest), typeof(ToolInvocationOutcome), statuses: new[] { 200, 202 });
            routes.Scoped(HttpMethod.GET, _Base + "/{id}", ResourceTypeEnum.ToolInvocation, OperationTypeEnum.Read, ReadAsync, "Read a tool invocation", "Tool Invocations", null, typeof(ToolInvocation));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            ToolInvocationFilter filter = new ToolInvocationFilter();
            filter.AgentProfileId = RouteHelpers.Query(http, "agentProfileId");
            filter.ResourceId = RouteHelpers.Query(http, "resourceId");
            filter.ToolName = RouteHelpers.Query(http, "toolName");
            filter.ConversationId = RouteHelpers.Query(http, "conversationId");
            filter.PrincipalId = RouteHelpers.Query(http, "principalId");
            filter.Decision = RouteHelpers.QueryEnum<InvocationDecisionEnum>(http, "decision");
            filter.Status = RouteHelpers.QueryEnum<InvocationStatusEnum>(http, "status");
            filter.Surface = RouteHelpers.QueryEnum<InvocationSurfaceEnum>(http, "surface");
            filter.SinceUtc = RouteHelpers.QueryDate(http, "fromUtc") ?? RouteHelpers.QueryDate(http, "since");
            filter.UntilUtc = RouteHelpers.QueryDate(http, "toUtc");
            filter.Effect = RouteHelpers.QueryEnum<EffectEnum>(http, "effect");
            EnumerationResult<ToolInvocation> page = await _Server.Database.ToolInvocations.EnumerateAsync(RouteHelpers.Tenant(http), RouteHelpers.Enumeration(http), filter, http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, page).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Server.Database.ToolInvocations.SummaryAsync(RouteHelpers.Tenant(http), RouteHelpers.QueryDate(http, "since"), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            ToolInvocation record = await _Server.Database.ToolInvocations.ReadAsync(RouteHelpers.Tenant(http), RouteHelpers.Param(http, "id")!, http.Token).ConfigureAwait(false) ?? throw new NotFoundException("Tool invocation not found.");
            await RouteHelpers.JsonAsync(http, 200, record).ConfigureAwait(false);
        }

        private async Task DryRunAsync(HttpContextBase http)
        {
            ToolInvocationOutcome outcome = await RunAsync(http, true).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, 200, outcome).ConfigureAwait(false);
        }

        private async Task InvokeAsync(HttpContextBase http)
        {
            ToolInvocationOutcome outcome = await RunAsync(http, false).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, outcome.Decision == InvocationDecisionEnum.ApprovalRequired ? 202 : 200, outcome).ConfigureAwait(false);
        }

        private async Task<ToolInvocationOutcome> RunAsync(HttpContextBase http, bool dryRun)
        {
            RequestContext ctx = RouteHelpers.Context(http);
            string tenantId = RouteHelpers.Tenant(http);
            ToolCallRequest body = RouteHelpers.RequiredBody<ToolCallRequest>(http);
            string? explicitProfile = !String.IsNullOrWhiteSpace(body.ProfileId) ? body.ProfileId : !String.IsNullOrWhiteSpace(body.Profile) ? body.Profile : http.Request.Headers[Hugo.Core.Constants.HeaderProfile];
            ProfileResolution resolution = await _Resolver.ResolveAsync(ctx, tenantId, explicitProfile, http.Token).ConfigureAwait(false);
            InvocationRequest request = new InvocationRequest();
            request.Principal = ctx;
            request.TenantId = tenantId;
            request.ProfileId = resolution.Profile.Id;
            request.ResourceAlias = body.Resource;
            request.ResourceId = body.ResourceId;
            request.ToolName = body.ToolName;
            request.ArgumentsJson = body.Arguments.HasValue ? body.Arguments.Value.GetRawText() : "{}";
            request.Surface = InvocationSurfaceEnum.Rest;
            request.DryRun = dryRun;
            request.WaitMode = ApprovalWaitModeEnum.NoWait;
            request.CorrelationId = ctx.RequestId;
            return await _Invocations.InvokeAsync(request, http.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
