namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Runs MCP tools for a caller (P7.5, P7.6, P7.7, P7.10): connector tools through <see cref="ToolInvocationService"/>
    /// with surface Mcp and a bounded approval wait, the hugo_* meta tools, ask_agent_smith, and the management tools.
    /// Every tool is checked against <see cref="McpToolAuthorizationRegistry"/> first; failures are isError results with
    /// stable codes. Thread-safe.
    /// </summary>
    public class McpToolExecutor
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly McpToolAuthorizationRegistry _Registry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="registry">Tool authorization registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public McpToolExecutor(ServerContext context, McpToolAuthorizationRegistry registry)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a tool for a caller.
        /// </summary>
        /// <param name="caller">Caller, or null when the request carried none.</param>
        /// <param name="name">Tool name.</param>
        /// <param name="args">Arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tool result (isError on failure).</returns>
        public async Task<McpToolCallResult> ExecuteAsync(McpCaller? caller, string name, RpcParameters? args, CancellationToken token)
        {
            try
            {
                if (caller == null) throw new McpToolErrorException("unauthenticated", "The request carried no authenticated caller.");
                McpToolAuthorization entry = _Registry.Find(name) ?? throw new McpToolErrorException("unmapped_tool", "Tool " + name + " has no authorization mapping and cannot be called.");
                await GateAsync(caller, entry, token).ConfigureAwait(false);
                string raw = String.IsNullOrWhiteSpace(args?.RawJson) ? "{}" : args!.RawJson!;
                switch (entry.Kind)
                {
                    case McpToolKindEnum.Connector:
                        return await ConnectorAsync(caller, name, raw, token).ConfigureAwait(false);
                    case McpToolKindEnum.Delegation:
                        return await AskAsync(caller, Arguments<McpAskArguments>(raw), token).ConfigureAwait(false);
                    case McpToolKindEnum.Management:
                        return await ManagementAsync(caller, name, raw, token).ConfigureAwait(false);
                    default:
                        return await MetaAsync(caller, name, raw, token).ConfigureAwait(false);
                }
            }
            catch (McpToolErrorException e)
            {
                return Error(e.Code, e.Message);
            }
            catch (NotFoundException e)
            {
                return Error("not_found", e.Message);
            }
            catch (ForbiddenException e)
            {
                return Error(Snake(e.Code), e.Message);
            }
            catch (ValidationException e)
            {
                return Error("invalid_argument", e.Describe());
            }
            catch (ConflictException e)
            {
                return Error(Snake(e.Code), e.Message);
            }
            catch (Exception e) when (e is ArgumentException || e is JsonException || e is FormatException)
            {
                return Error("invalid_argument", e.Message);
            }
            catch (Exception e) when (DatabaseFailures.Classify(e) != DatabaseFailureEnum.None)
            {
                return Error("unavailable", "The database is not available right now; retry in a few seconds.");
            }
        }

        /// <summary>
        /// Whether management tools are available to a caller: the profile has expose_management_tools and the principal
        /// is Admin-capable (a system or tenant administrator, or a principal allowed to create resources or update
        /// bindings).
        /// </summary>
        /// <param name="principal">Principal.</param>
        /// <param name="profile">Profile.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when available.</returns>
        public async Task<bool> ManagementAllowedAsync(RequestContext principal, AgentProfile profile, CancellationToken token)
        {
            if (principal == null || profile == null || !profile.ExposeManagementTools) return false;
            // A credential carries its owner's admin flags but may be scoped down by a role, so credentials are judged by
            // the authorization service alone.
            if (principal.PrincipalType != PrincipalTypeEnum.Credential && (principal.IsAdmin || principal.IsTenantAdmin || principal.PrincipalType == PrincipalTypeEnum.Administrator)) return true;
            AuthorizationDecision create = await _Context.Authorization.AuthorizeAsync(principal, profile.TenantId, ResourceTypeEnum.Resource, OperationTypeEnum.Create, null, token).ConfigureAwait(false);
            if (create.Permitted) return true;
            AuthorizationDecision update = await _Context.Authorization.AuthorizeAsync(principal, profile.TenantId, ResourceTypeEnum.Binding, OperationTypeEnum.Update, null, token).ConfigureAwait(false);
            return update.Permitted;
        }

        /// <summary>
        /// Whether ask_agent_smith is available for a profile: a delegation service is registered and the profile has an
        /// inference endpoint.
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <returns>True when available.</returns>
        public bool DelegationAvailable(AgentProfile profile)
        {
            return _Context.AgentDelegation != null && profile != null && !String.IsNullOrEmpty(profile.InferenceEndpointId);
        }

        /// <summary>
        /// The caller's bound resources.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resources ordered by alias.</returns>
        public async Task<List<McpBoundResource>> BoundAsync(McpCaller caller, CancellationToken token)
        {
            List<BoundResource> bound = await _Context.Get<ProfileBindingResolver>().ResolveAsync(caller.TenantId, caller.Profile.Id, caller.Principal, token).ConfigureAwait(false);
            return bound.OrderBy(b => b.Binding.Alias, StringComparer.Ordinal).Select(b => new McpBoundResource
            {
                Alias = b.Binding.Alias,
                ConnectorType = b.Resource.ConnectorType,
                Connector = String.IsNullOrEmpty(b.Connector.Descriptor.DisplayName) ? b.Resource.ConnectorType : b.Connector.Descriptor.DisplayName,
                Family = b.Family,
                Environment = String.IsNullOrEmpty(b.Resource.Environment) ? null : b.Resource.Environment,
                AccessMode = b.EffectiveAccessMode,
                Description = String.IsNullOrWhiteSpace(b.Resource.Description) ? null : b.Resource.Description,
                Notes = String.IsNullOrWhiteSpace(b.Resource.Notes) ? null : b.Resource.Notes
            }).ToList();
        }

        /// <summary>
        /// The full guidance for one bound resource (hugo_describe_resource, hugo://resources/{alias}).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="alias">Alias.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Guidance.</returns>
        /// <exception cref="McpToolErrorException">Thrown when no usable binding has the alias.</exception>
        public async Task<ResourceInstructions> DescribeAsync(McpCaller caller, string? alias, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(alias)) throw new McpToolErrorException("invalid_argument", "resource is required.");
            ResourceInstructions? described = await _Context.Get<InstructionCompiler>().DescribeResourceAsync(caller.TenantId, caller.Profile.Id, alias.Trim(), caller.Principal, InvocationSurfaceEnum.Mcp, token).ConfigureAwait(false);
            if (described == null)
            {
                List<McpBoundResource> bound = await BoundAsync(caller, token).ConfigureAwait(false);
                throw new McpToolErrorException("not_found", "No resource '" + alias + "' is bound to profile " + caller.Profile.Name + "." + (bound.Count > 0 ? " Available: " + String.Join(", ", bound.Select(b => b.Alias)) + "." : String.Empty));
            }
            return described;
        }

        /// <summary>
        /// Render a resource's guidance as Markdown.
        /// </summary>
        /// <param name="r">Guidance.</param>
        /// <returns>Markdown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when r is null.</exception>
        public static string DescribeText(ResourceInstructions r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            StringBuilder sb = new StringBuilder();
            sb.Append("## ").Append(r.Alias).Append(" (").Append(r.ConnectorDisplayName);
            if (!String.IsNullOrEmpty(r.Environment)) sb.Append(", environment: ").Append(r.Environment);
            sb.Append(", access: ").Append(r.AccessMode).Append(")\n\n");
            if (!String.IsNullOrEmpty(r.Description)) sb.Append(r.Description).Append("\n\n");
            if (!String.IsNullOrEmpty(r.Instructions)) sb.Append(r.Instructions).Append("\n\n");
            if (!String.IsNullOrEmpty(r.Notes)) sb.Append("Operator notes: ").Append(r.Notes).Append("\n\n");
            if (!String.IsNullOrEmpty(r.Knowledge)) sb.Append("Knowledge:\n").Append(r.Knowledge).Append("\n\n");
            sb.Append("Tools you can use on ").Append(r.Alias).Append(": ").Append(r.Tools.Count == 0 ? "none" : String.Join(", ", r.Tools)).Append(".\n");
            if (r.MaxRows.HasValue || r.MaxOutputBytes.HasValue)
            {
                sb.Append("Limits:");
                if (r.MaxRows.HasValue) sb.Append(" at most ").Append(r.MaxRows.Value).Append(" rows");
                if (r.MaxRows.HasValue && r.MaxOutputBytes.HasValue) sb.Append(',');
                if (r.MaxOutputBytes.HasValue) sb.Append(" at most ").Append(r.MaxOutputBytes.Value).Append(" bytes of output");
                sb.Append(".\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The caller's approvals, newest first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="status">Status filter, or null.</param>
        /// <param name="maxResults">Most approvals (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approvals.</returns>
        public async Task<McpApprovalList> ApprovalsAsync(McpCaller caller, ApprovalStatusEnum? status, int maxResults, CancellationToken token)
        {
            ToolInvocationFilter filter = new ToolInvocationFilter();
            filter.PrincipalId = caller.Principal.PrincipalId ?? "admin";
            filter.ApprovalStatus = status;
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = Math.Clamp(maxResults, 1, 100);
            EnumerationResult<Approval> page = await _Context.Database.Approvals.EnumerateAsync(caller.TenantId, query, filter, token).ConfigureAwait(false);
            McpApprovalList list = new McpApprovalList();
            list.Approvals = page.Objects.Select(McpApprovalSummary.From).ToList();
            list.TotalRecords = page.TotalRecords;
            return list;
        }

        /// <summary>
        /// Build a structured tool result: the readable JSON as text, and the object as structuredContent.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="text">Text, or null for the readable JSON.</param>
        /// <returns>Result.</returns>
        public static McpToolCallResult Structured(object value, string? text = null)
        {
            McpToolCallResult result = McpToolCallResult.FromText(text ?? Serializer.SerializeReadable(value));
            result.StructuredContent = Element(Serializer.Serialize(value));
            return result;
        }

        /// <summary>
        /// Build an isError tool result with a stable code.
        /// </summary>
        /// <param name="code">Code.</param>
        /// <param name="message">Message.</param>
        /// <param name="retryAfterSeconds">Seconds to wait before retrying, or null.</param>
        /// <returns>Result.</returns>
        public static McpToolCallResult Error(string code, string message, double? retryAfterSeconds = null)
        {
            string text = message ?? String.Empty;
            if (!text.StartsWith(code + ":", StringComparison.Ordinal)) text = code + ": " + text;
            McpToolCallResult result = McpToolCallResult.FromText(text);
            result.IsError = true;
            Dictionary<string, object?> structured = new Dictionary<string, object?>();
            structured["error"] = code;
            structured["message"] = message;
            if (retryAfterSeconds.HasValue) structured["retryAfterSeconds"] = retryAfterSeconds.Value;
            result.StructuredContent = Element(Serializer.Serialize(structured));
            return result;
        }

        /// <summary>
        /// Convert an invocation outcome to an MCP tool result: text, structuredContent when the result has structured
        /// JSON, isError with the stable code on failure, and the non-error approval_pending result while pending.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcome is null.</exception>
        public static McpToolCallResult FromOutcome(ToolInvocationOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            ToolResult tool = outcome.Result ?? ToolResult.Error(outcome.DecisionCode ?? "not_executed", outcome.Reason ?? "The call did not run.");
            if (tool.IsError) return Error(tool.ErrorCode ?? outcome.DecisionCode ?? "error", tool.Text, tool.RetryAfterSeconds);
            McpToolCallResult result = McpToolCallResult.FromText(tool.Text + (tool.Truncated ? "\n[output truncated to the call's limits]" : String.Empty));
            if (!String.IsNullOrWhiteSpace(tool.StructuredJson))
            {
                JsonElement? element = Element(tool.StructuredJson);
                if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object) result.StructuredContent = element.Value;
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task GateAsync(McpCaller caller, McpToolAuthorization entry, CancellationToken token)
        {
            if (entry.Kind == McpToolKindEnum.Management && !await ManagementAllowedAsync(caller.Principal, caller.Profile, token).ConfigureAwait(false))
                throw new McpToolErrorException("forbidden", "Management tools need a profile with expose_management_tools and an Admin-capable principal.");
            if (entry.Kind == McpToolKindEnum.Delegation && !DelegationAvailable(caller.Profile))
                throw new McpToolErrorException("not_supported", "ask_agent_smith needs Hugo's agent runtime and an inference endpoint on profile " + caller.Profile.Name + ".");
            string? resourceId = entry.Kind == McpToolKindEnum.Management ? null : caller.Profile.Id;
            AuthorizationDecision decision = await _Context.Authorization.AuthorizeAsync(caller.Principal, caller.TenantId, entry.ResourceType, entry.Operation, resourceId, token).ConfigureAwait(false);
            if (!decision.Permitted) throw new McpToolErrorException("forbidden", "Not permitted: " + entry.Operation + " on " + entry.ResourceType + " is required for " + entry.Name + ".");
        }

        private async Task<McpToolCallResult> ConnectorAsync(McpCaller caller, string name, string raw, CancellationToken token)
        {
            ToolInvocationService invocations = _Context.Get<ToolInvocationService>();
            using (McpApprovalProgress progress = new McpApprovalProgress(McpToolCallContext.Current, TimeSpan.FromSeconds(_Context.Settings.Mcp.ProgressIntervalSeconds)))
            {
                InvocationRequest request = new InvocationRequest();
                request.Principal = caller.Principal;
                request.TenantId = caller.TenantId;
                request.ProfileId = caller.Profile.Id;
                request.ToolName = name;
                request.ArgumentsJson = raw;
                request.Surface = InvocationSurfaceEnum.Mcp;
                request.WaitMode = ApprovalWaitModeEnum.WaitWithTimeout;
                request.WaitTimeout = TimeSpan.FromSeconds(_Context.Settings.Approvals.McpWaitSeconds);
                request.ApprovalListener = progress;
                request.CorrelationId = caller.Principal.RequestId;
                ToolInvocationOutcome outcome = await invocations.InvokeAsync(request, token).ConfigureAwait(false);
                return FromOutcome(outcome);
            }
        }

        private async Task<McpToolCallResult> MetaAsync(McpCaller caller, string name, string raw, CancellationToken token)
        {
            if (name == McpToolCatalog.Whoami) return Structured(await WhoamiAsync(caller, token).ConfigureAwait(false));
            if (name == McpToolCatalog.GetInstructions)
            {
                CompiledInstructions compiled = await _Context.Get<InstructionCompiler>().CompileAsync(caller.TenantId, caller.Profile.Id, caller.Principal, InvocationSurfaceEnum.Mcp, token).ConfigureAwait(false);
                return McpToolCallResult.FromText(compiled.Text);
            }
            if (name == McpToolCatalog.ListResources)
            {
                McpResourceList list = new McpResourceList();
                list.Profile = caller.Profile.Name;
                list.Resources = await BoundAsync(caller, token).ConfigureAwait(false);
                return Structured(list);
            }
            if (name == McpToolCatalog.DescribeResource)
            {
                ResourceInstructions described = await DescribeAsync(caller, Arguments<McpResourceArguments>(raw).Resource, token).ConfigureAwait(false);
                return Structured(described, DescribeText(described));
            }
            if (name == McpToolCatalog.ListApprovals)
            {
                McpListApprovalsArguments arguments = Arguments<McpListApprovalsArguments>(raw);
                return Structured(await ApprovalsAsync(caller, arguments.Status, arguments.MaxResults ?? 25, token).ConfigureAwait(false));
            }
            if (name == McpToolCatalog.AwaitApproval) return await AwaitApprovalAsync(caller, Arguments<McpApprovalArguments>(raw), token).ConfigureAwait(false);
            throw new McpToolErrorException("tool_not_found", "Unknown tool " + name + ".");
        }

        private async Task<McpWhoami> WhoamiAsync(McpCaller caller, CancellationToken token)
        {
            Tenant? tenant = await _Context.Database.Tenants.ReadAsync(caller.TenantId, token).ConfigureAwait(false);
            McpWhoami who = new McpWhoami();
            who.TenantId = caller.TenantId;
            who.TenantName = tenant?.Name ?? caller.TenantId;
            who.PrincipalType = caller.Principal.PrincipalType.ToString();
            who.PrincipalId = caller.Principal.PrincipalId;
            who.PrincipalName = caller.Principal.PrincipalName;
            who.ProfileId = caller.Profile.Id;
            who.Profile = caller.Profile.Name;
            who.ProfileSource = caller.ProfileSource;
            who.PersonaName = caller.Profile.PersonaName;
            who.UserAddress = caller.Profile.UserAddress;
            who.Resources = await BoundAsync(caller, token).ConfigureAwait(false);
            who.AskAgentSmith = DelegationAvailable(caller.Profile);
            who.ManagementTools = await ManagementAllowedAsync(caller.Principal, caller.Profile, token).ConfigureAwait(false);
            if (who.Resources.Count == 0) who.AccessSummary = "No resources are bound to profile " + caller.Profile.Name + ".";
            else who.AccessSummary = who.Resources.Count + " resource" + (who.Resources.Count == 1 ? String.Empty : "s") + ": "
                + String.Join(", ", who.Resources.GroupBy(r => r.AccessMode).OrderBy(g => g.Key).Select(g => g.Count() + " " + g.Key)) + ".";
            return who;
        }

        private async Task<McpToolCallResult> AwaitApprovalAsync(McpCaller caller, McpApprovalArguments arguments, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(arguments.ApprovalId)) throw new McpToolErrorException("invalid_argument", "approvalId is required.");
            if (arguments.WaitSeconds.HasValue && (arguments.WaitSeconds.Value < 0 || arguments.WaitSeconds.Value > 600)) throw new McpToolErrorException("invalid_argument", "waitSeconds must be between 0 and 600.");
            string id = arguments.ApprovalId.Trim();
            Approval? approval = await _Context.Database.Approvals.ReadAsync(caller.TenantId, id, token).ConfigureAwait(false);
            if (approval == null) throw new McpToolErrorException("not_found", "Approval " + id + " not found.");
            bool requester = !String.IsNullOrEmpty(approval.RequestedByPrincipalId) && String.Equals(approval.RequestedByPrincipalId, caller.Principal.PrincipalId, StringComparison.Ordinal);
            if (!requester)
            {
                AuthorizationDecision read = await _Context.Authorization.AuthorizeAsync(caller.Principal, caller.TenantId, ResourceTypeEnum.Approval, OperationTypeEnum.Read, approval.Id, token).ConfigureAwait(false);
                if (!read.Permitted) throw new McpToolErrorException("not_found", "Approval " + id + " not found.");
            }
            TimeSpan wait = TimeSpan.FromSeconds(arguments.WaitSeconds ?? _Context.Settings.Approvals.McpWaitSeconds);
            using (McpApprovalProgress progress = new McpApprovalProgress(McpToolCallContext.Current, TimeSpan.FromSeconds(_Context.Settings.Mcp.ProgressIntervalSeconds)))
            {
                if (approval.Status == ApprovalStatusEnum.Pending) progress.Start(approval.Id, approval.Summary);
                ToolInvocationOutcome outcome = await _Context.Get<ToolInvocationService>().AwaitApprovalAsync(caller.Principal, caller.TenantId, approval.Id, wait, token).ConfigureAwait(false);
                return FromOutcome(outcome);
            }
        }

        private async Task<McpToolCallResult> AskAsync(McpCaller caller, McpAskArguments arguments, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(arguments.Message)) throw new McpToolErrorException("invalid_argument", "message is required.");
            IAgentDelegationService delegation = _Context.AgentDelegation ?? throw new McpToolErrorException("not_supported", "ask_agent_smith is not available on this server.");
            AgentDelegationResult result = await delegation.AskAsync(caller.Principal, caller.TenantId, caller.Profile.Id, arguments.Message, String.IsNullOrWhiteSpace(arguments.ConversationId) ? null : arguments.ConversationId.Trim(), token).ConfigureAwait(false);
            McpAgentAnswer answer = new McpAgentAnswer();
            answer.Answer = result.FinalText;
            answer.ConversationId = result.ConversationId;
            answer.ApprovalId = result.ApprovalId;
            answer.ToolTrace = result.ToolTrace;
            if (result.Status == AgentDelegationStatusEnum.Failed) throw new McpToolErrorException("agent_failed", String.IsNullOrEmpty(result.FinalText) ? "Agent Smith could not complete the task." : result.FinalText);
            StringBuilder text = new StringBuilder();
            if (result.Status == AgentDelegationStatusEnum.ApprovalPending)
            {
                answer.Status = "approval_pending";
                answer.Next = "approval " + result.ApprovalId + " must be decided by a human; then call ask_agent_smith again with conversationId " + result.ConversationId;
                text.Append("Agent Smith is waiting for approval ").Append(result.ApprovalId).Append(". Next: ").Append(answer.Next).Append('.');
                if (!String.IsNullOrEmpty(result.FinalText)) text.Append("\n\n").Append(result.FinalText);
            }
            else
            {
                answer.Status = "completed";
                text.Append(result.FinalText);
            }
            if (result.ToolTrace.Count > 0) text.Append("\n\nTool trace:\n").Append(String.Join("\n", result.ToolTrace.Select(t => "- " + t)));
            if (!String.IsNullOrEmpty(result.ConversationId)) text.Append("\n\nconversationId: ").Append(result.ConversationId);
            return Structured(answer, text.ToString());
        }

        private async Task<McpToolCallResult> ManagementAsync(McpCaller caller, string name, string raw, CancellationToken token)
        {
            RequestContext ctx = caller.Principal;
            string tenantId = caller.TenantId;
            if (name == McpToolCatalog.AdminListProfiles)
            {
                McpListArguments arguments = Arguments<McpListArguments>(raw);
                return Structured(await _Context.Get<AgentProfileService>().EnumerateAsync(ctx, tenantId, Query(arguments), token).ConfigureAwait(false));
            }
            if (name == McpToolCatalog.AdminDescribeProfile)
            {
                AgentProfile profile = await ProfileAsync(caller, Arguments<McpProfileArguments>(raw).Profile, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read, token).ConfigureAwait(false);
                McpProfileDetail detail = new McpProfileDetail();
                detail.Profile = profile;
                AuthorizationDecision bindings = await _Context.Authorization.AuthorizeAsync(ctx, tenantId, ResourceTypeEnum.Binding, OperationTypeEnum.Read, null, token).ConfigureAwait(false);
                if (bindings.Permitted) detail.Bindings = await _Context.Get<BindingService>().ListAsync(ctx, tenantId, profile.Id, token).ConfigureAwait(false);
                return Structured(detail);
            }
            if (name == McpToolCatalog.AdminListResources)
            {
                McpListArguments arguments = Arguments<McpListArguments>(raw);
                return Structured(await _Context.Get<ResourceService>().EnumerateAsync(ctx, tenantId, Query(arguments), String.IsNullOrWhiteSpace(arguments.ConnectorType) ? null : arguments.ConnectorType.Trim(), token).ConfigureAwait(false));
            }
            if (name == McpToolCatalog.AdminDescribeResource)
            {
                string reference = Arguments<McpResourceArguments>(raw).Resource ?? throw new McpToolErrorException("invalid_argument", "resource is required.");
                Resource? found = reference.StartsWith(Constants.ResourceIdPrefix, StringComparison.Ordinal) ? await _Context.Database.Resources.ReadAsync(tenantId, reference.Trim(), token).ConfigureAwait(false) : null;
                if (found == null) found = await _Context.Database.Resources.ReadByNameAsync(tenantId, reference.Trim(), token).ConfigureAwait(false);
                if (found == null || !ResourceVisibility.CanSee(found, ctx)) throw new McpToolErrorException("not_found", "Resource " + reference + " not found.");
                await RequireAsync(ctx, tenantId, ResourceTypeEnum.Resource, OperationTypeEnum.Read, found.Id, token).ConfigureAwait(false);
                ResourceService resources = _Context.Get<ResourceService>();
                McpResourceDetail detail = new McpResourceDetail();
                detail.Resource = await resources.ReadAsync(ctx, tenantId, found.Id, token).ConfigureAwait(false);
                detail.Referrers = await resources.ReferrersAsync(ctx, tenantId, found.Id, token).ConfigureAwait(false);
                return Structured(detail);
            }
            if (name == McpToolCatalog.AdminListBindings)
            {
                AgentProfile profile = await ProfileAsync(caller, Arguments<McpProfileArguments>(raw).Profile, ResourceTypeEnum.Binding, OperationTypeEnum.Read, token).ConfigureAwait(false);
                return Structured(new Dictionary<string, object> { { "profile", profile.Name }, { "bindings", await _Context.Get<BindingService>().ListAsync(ctx, tenantId, profile.Id, token).ConfigureAwait(false) } });
            }
            if (name == McpToolCatalog.AdminCreateResource)
            {
                ResourceRequest request = Arguments<ResourceRequest>(raw);
                Resource created = await _Context.Get<ResourceService>().CreateAsync(ctx, tenantId, request, token).ConfigureAwait(false);
                return Structured(created, "Created resource " + created.Name + " (" + created.Id + ", " + created.ConnectorType + ").\n\n" + Serializer.SerializeReadable(created));
            }
            if (name == McpToolCatalog.AdminUpdateBinding)
            {
                McpUpdateBindingArguments arguments = Arguments<McpUpdateBindingArguments>(raw);
                if (!arguments.AccessMode.HasValue) throw new McpToolErrorException("invalid_argument", "accessMode is required.");
                if (String.IsNullOrWhiteSpace(arguments.Binding)) throw new McpToolErrorException("invalid_argument", "binding is required.");
                AgentProfile profile = await ProfileAsync(caller, arguments.Profile, ResourceTypeEnum.Binding, OperationTypeEnum.Update, token).ConfigureAwait(false);
                BindingService bindingService = _Context.Get<BindingService>();
                List<Binding> bindings = await bindingService.ListAsync(ctx, tenantId, profile.Id, token).ConfigureAwait(false);
                string reference = arguments.Binding.Trim();
                Binding binding = bindings.FirstOrDefault(b => String.Equals(b.Id, reference, StringComparison.Ordinal) || String.Equals(b.Alias, reference, StringComparison.Ordinal))
                    ?? throw new McpToolErrorException("not_found", "Binding " + reference + " not found on profile " + profile.Name + ".");
                await RequireAsync(ctx, tenantId, ResourceTypeEnum.Binding, OperationTypeEnum.Update, binding.Id, token).ConfigureAwait(false);
                BindingRequest request = new BindingRequest();
                request.AccessMode = arguments.AccessMode;
                request.RequireApprovalForDestructive = arguments.RequireApprovalForDestructive;
                request.Enabled = arguments.Enabled;
                Binding updated = await bindingService.UpdateAsync(ctx, tenantId, profile.Id, binding.Id, request, token).ConfigureAwait(false);
                return Structured(updated, "Binding " + updated.Alias + " of profile " + profile.Name + " is now " + updated.AccessMode + ".\n\n" + Serializer.SerializeReadable(updated));
            }
            throw new McpToolErrorException("tool_not_found", "Unknown tool " + name + ".");
        }

        private async Task<AgentProfile> ProfileAsync(McpCaller caller, string? reference, ResourceTypeEnum resourceType, OperationTypeEnum operation, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(reference)) throw new McpToolErrorException("invalid_argument", "profile is required.");
            string value = reference.Trim();
            AgentProfile? profile = value.StartsWith(Constants.AgentProfileIdPrefix, StringComparison.Ordinal) ? await _Context.Database.AgentProfiles.ReadAsync(caller.TenantId, value, token).ConfigureAwait(false) : null;
            if (profile == null) profile = await _Context.Database.AgentProfiles.ReadByNameAsync(caller.TenantId, value, token).ConfigureAwait(false);
            if (profile == null || !ResourceVisibility.CanSee(profile, caller.Principal)) throw new McpToolErrorException("not_found", "Agent profile " + value + " not found.");
            await RequireAsync(caller.Principal, caller.TenantId, resourceType, operation, resourceType == ResourceTypeEnum.AgentProfile ? profile.Id : null, token).ConfigureAwait(false);
            return profile;
        }

        private async Task RequireAsync(RequestContext ctx, string tenantId, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId, CancellationToken token)
        {
            AuthorizationDecision decision = await _Context.Authorization.AuthorizeAsync(ctx, tenantId, resourceType, operation, resourceId, token).ConfigureAwait(false);
            if (!decision.Permitted) throw new McpToolErrorException("forbidden", "Not permitted: " + operation + " on " + resourceType + " is required.");
        }

        private static EnumerationQuery Query(McpListArguments arguments)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = Math.Clamp(arguments.MaxResults ?? 25, 1, 100);
            query.Search = String.IsNullOrWhiteSpace(arguments.Search) ? null : arguments.Search.Trim();
            return query;
        }

        private static T Arguments<T>(string raw) where T : class, new()
        {
            try
            {
                return Serializer.Deserialize<T>(raw) ?? new T();
            }
            catch (JsonException e)
            {
                throw new McpToolErrorException("invalid_argument", "The arguments do not match the tool's input schema: " + e.Message);
            }
        }

        private static JsonElement? Element(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    return document.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Snake(string code)
        {
            if (String.IsNullOrEmpty(code)) return "error";
            StringBuilder sb = new StringBuilder(code.Length + 4);
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (Char.IsUpper(c))
                {
                    if (i > 0 && code[i - 1] != '_') sb.Append('_');
                    sb.Append(Char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion
    }
}
