namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Inference;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Responses;
    using Hugo.Core.Safety;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// The single entry point for every tool call from every surface (P3.13, plan section 8.6). Steps: (1) resolve the
    /// profile, binding (by alias or resource identifier), resource, connector, and tool; (2) authorize Execute on the
    /// profile (or on the resource for an administrator resource invoke) and enforce credential lock_to_profile; (4)
    /// validate the arguments against the tool schema; binding deny patterns on the primary argument of per-call
    /// classified tools; (5) classify; (3, 6) tool rules, the access mode matrix with the resource ceiling, and tenant
    /// lockdown through <see cref="AccessPolicy"/>; (7) approval, waiting as the surface requires; (8) secrets from the
    /// vault for this call only; (9) execute with the timeout, output limits, per-resource concurrency cap, and the
    /// profile's model when it has one; (10) scrub with the call's secret values and bound the output; (11) record
    /// (Denied and approval rows synchronously, executed calls through the background recorder), metrics, span, and
    /// the tool.completed event; (12) return. Thread-safe.
    /// </summary>
    public class ToolInvocationService
    {
        #region Public-Members

        /// <summary>
        /// Characters of output kept as the record's preview. Default 4096, minimum 0, maximum 65536.
        /// </summary>
        public int PreviewLength
        {
            get
            {
                return _PreviewLength;
            }
            set
            {
                _PreviewLength = Math.Clamp(value, 0, 65536);
            }
        }

        /// <summary>
        /// Minutes a result of an approved call is kept in memory for <see cref="AwaitApprovalAsync"/>. Default 30.
        /// </summary>
        public int CompletedResultMinutes { get; set; } = 30;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ConnectorRegistry _Registry;
        private readonly AuthorizationService _Authorization;
        private readonly ConnectorContextBuilder _Contexts;
        private readonly ConnectorSessionPool _Pool;
        private readonly ApprovalService _Approvals;
        private readonly ModelEndpointService _Models;
        private readonly ToolInvocationRecorder _Recorder;
        private readonly HugoSettings _Settings;
        private readonly HugoLog _Logging;
        private readonly EventHub? _Events;
        private readonly ConcurrentDictionary<string, string> _PendingArguments = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ToolInvocationOutcome> _Completed = new ConcurrentDictionary<string, ToolInvocationOutcome>(StringComparer.Ordinal);
        private int _PreviewLength = 4096;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="registry">Connector registry.</param>
        /// <param name="authorization">Authorization.</param>
        /// <param name="contexts">Connector context builder.</param>
        /// <param name="pool">Session pool and concurrency gates.</param>
        /// <param name="approvals">Approvals.</param>
        /// <param name="models">Model endpoints.</param>
        /// <param name="recorder">Background record writer.</param>
        /// <param name="settings">Server settings (Connectors and Approvals are read on every call).</param>
        /// <param name="logging">Logging.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ToolInvocationService(DatabaseDriverBase database, ConnectorRegistry registry, AuthorizationService authorization, ConnectorContextBuilder contexts, ConnectorSessionPool pool, ApprovalService approvals, ModelEndpointService models, ToolInvocationRecorder recorder, HugoSettings settings, HugoLog logging, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _Pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _Approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
            _Models = models ?? throw new ArgumentNullException(nameof(models));
            _Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Events = events;
            _Approvals.Unattended = OnUnattendedDecisionAsync;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one tool call through the pipeline. Every surface calls this; no surface calls a connector directly.
        /// Failures of the call itself are outcomes, not exceptions.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token; cancels approval waits and the in-flight call.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
        public async Task<ToolInvocationOutcome> InvokeAsync(InvocationRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            InvocationState state = NewState(request);

            // 1. Resolve.
            if (String.IsNullOrWhiteSpace(request.ToolName)) return await DenyAsync(state, "tool_not_found", "A tool name is required.", token).ConfigureAwait(false);
            JsonObject? arguments = ParseArguments(request.ArgumentsJson);
            if (arguments == null) return await DenyAsync(state, ConnectorErrorCodes.InvalidArgument, "Arguments must be a JSON object.", token).ConfigureAwait(false);
            string? alias = request.ResourceAlias;
            if (String.IsNullOrEmpty(alias) && arguments.TryGetPropertyValue("resource", out JsonNode? resourceNode) && resourceNode is JsonValue resourceValue && resourceValue.TryGetValue(out string? fromArgs)) alias = fromArgs;
            arguments.Remove("resource");
            state.ArgumentsJson = arguments.ToJsonString();
            state.Record.BindingAlias = alias;

            Tenant? tenant = await _Database.Tenants.ReadAsync(request.TenantId, token).ConfigureAwait(false);
            if (tenant == null) return await DenyAsync(state, "tenant_not_found", "Tenant not found.", token).ConfigureAwait(false);

            if (!request.AdminResourceInvoke)
            {
                AgentProfile? profile = String.IsNullOrEmpty(request.ProfileId) ? null : await _Database.AgentProfiles.ReadAsync(request.TenantId, request.ProfileId, token).ConfigureAwait(false);
                if (profile == null || !profile.Active || !ResourceVisibility.CanSee(profile, request.Principal)) return await DenyAsync(state, "profile_not_found", "Agent profile not found.", token).ConfigureAwait(false);
                state.Profile = profile;
                state.Record.AgentProfileId = profile.Id;

                // 2. Authorize before revealing anything about the profile's bindings.
                AuthorizationDecision decision = await _Authorization.AuthorizeAsync(request.Principal, request.TenantId, ResourceTypeEnum.AgentProfile, OperationTypeEnum.Execute, profile.Id, token).ConfigureAwait(false);
                if (!decision.Permitted) return await DenyAsync(state, "forbidden", "The caller may not use agent profile " + profile.Name + " (Execute on AgentProfile is required).", token).ConfigureAwait(false);
                Credential? credential = request.Principal.Credential;
                if (credential != null && credential.LockToProfile && !String.Equals(credential.AgentProfileId, profile.Id, StringComparison.Ordinal))
                    return await DenyAsync(state, "profile_locked", "This credential is locked to another agent profile.", token).ConfigureAwait(false);

                List<Binding> bindings = await _Database.Bindings.ListByProfileAsync(request.TenantId, profile.Id, token).ConfigureAwait(false);
                Binding? binding = !String.IsNullOrEmpty(alias)
                    ? bindings.FirstOrDefault(b => String.Equals(b.Alias, alias, StringComparison.Ordinal))
                    : (String.IsNullOrEmpty(request.ResourceId) ? null : bindings.FirstOrDefault(b => String.Equals(b.ResourceId, request.ResourceId, StringComparison.Ordinal)));
                if (binding == null || !binding.Enabled)
                {
                    string available = String.Join(", ", bindings.Where(b => b.Enabled).Select(b => b.Alias));
                    return await DenyAsync(state, "binding_not_found", "No enabled resource '" + (alias ?? request.ResourceId ?? String.Empty) + "' is bound to profile " + profile.Name + "." + (available.Length > 0 ? " Available: " + available + "." : String.Empty), token).ConfigureAwait(false);
                }
                state.Binding = binding;
                state.Record.BindingId = binding.Id;
                state.Record.BindingAlias = binding.Alias;
                Resource? resource = await _Database.Resources.ReadAsync(request.TenantId, binding.ResourceId, token).ConfigureAwait(false);
                if (resource == null || !resource.Active || !ResourceVisibility.CanSee(resource, request.Principal)) return await DenyAsync(state, "binding_not_found", "The resource bound as '" + binding.Alias + "' is not available.", token).ConfigureAwait(false);
                state.Resource = resource;
                state.AccessMode = binding.AccessMode;
            }
            else
            {
                string? resourceId = request.ResourceId;
                Resource? resource = null;
                if (!String.IsNullOrEmpty(resourceId)) resource = await _Database.Resources.ReadAsync(request.TenantId, resourceId, token).ConfigureAwait(false);
                else if (!String.IsNullOrEmpty(alias)) resource = await _Database.Resources.ReadByNameAsync(request.TenantId, alias, token).ConfigureAwait(false);
                if (resource == null || !ResourceVisibility.CanSee(resource, request.Principal)) return await DenyAsync(state, "binding_not_found", "Resource not found.", token).ConfigureAwait(false);
                AuthorizationDecision decision = await _Authorization.AuthorizeAsync(request.Principal, request.TenantId, ResourceTypeEnum.Resource, OperationTypeEnum.Execute, resource.Id, token).ConfigureAwait(false);
                if (!decision.Permitted) return await DenyAsync(state, "forbidden", "The caller may not invoke resource " + resource.Name + " directly (Execute on Resource is required).", token).ConfigureAwait(false);
                if (!resource.Active) return await DenyAsync(state, "binding_not_found", "Resource " + resource.Name + " is inactive.", token).ConfigureAwait(false);
                state.Resource = resource;
                state.Record.BindingAlias = resource.Name;
                state.AccessMode = resource.DefaultAccessModeCeiling ?? AccessModeEnum.ReadWriteWithApproval;
            }
            state.Record.ResourceId = state.Resource.Id;
            state.Record.ConnectorType = state.Resource.ConnectorType;
            state.Outcome.ResourceId = state.Resource.Id;
            state.Outcome.ConnectorType = state.Resource.ConnectorType;
            state.Outcome.BindingAlias = state.Record.BindingAlias;

            IConnector? connector = _Registry.Get(state.Resource.ConnectorType);
            if (connector == null) return await DenyAsync(state, "tool_not_found", "Connector type " + state.Resource.ConnectorType + " is not loaded on this server.", token).ConfigureAwait(false);
            IConnectorTool? tool = connector.Tools.FirstOrDefault(t => String.Equals(t.Descriptor.Name, request.ToolName, StringComparison.Ordinal));
            if (tool == null) return await DenyAsync(state, "tool_not_found", "Tool " + request.ToolName + " is not available on " + state.Record.BindingAlias + " (" + connector.Descriptor.DisplayName + ").", token).ConfigureAwait(false);
            state.Connector = connector;
            state.Tool = tool;

            // Saved-context writes change Hugo state even when the target-side tool effect is Read.
            // Apply this policy before classification and dry-run results, without changing target effects.
            if (tool.Descriptor.UpdatesKnowledge)
            {
                state.Record.Effect = tool.Descriptor.BaseEffect;
                state.Record.EffectReason = "Updates Hugo's saved resource context.";
                state.Outcome.Effect = state.Record.Effect;
                state.Outcome.EffectReason = state.Record.EffectReason;
                if (tenant.Lockdown)
                    return await DenyAsync(state, "tenant_lockdown", "The tenant is in lockdown; saved resource context updates are not allowed.", token).ConfigureAwait(false);
                if (state.Binding?.AllowKnowledgeUpdates == false)
                    return await DenyAsync(state, ConnectorErrorCodes.PermissionDenied, "Saved resource context updates are disabled for this resource binding.", token).ConfigureAwait(false);
            }

            // 4. Validate arguments against the tool schema.
            List<string> errors = ToolArgumentValidator.Validate(tool.Descriptor.InputSchemaJson, state.ArgumentsJson);
            if (errors.Count > 0) return await DenyAsync(state, ConnectorErrorCodes.InvalidArgument, "Invalid arguments for " + tool.Descriptor.Name + ": " + String.Join(" ", errors), token).ConfigureAwait(false);
            ToolArguments toolArguments = new ToolArguments(state.ArgumentsJson);

            // Binding deny patterns apply to the primary argument of per-call classified tools.
            if (state.Binding != null && tool.Descriptor.ClassifiedPerCall && state.Binding.DenyPatterns.Count > 0)
            {
                string? primary = ToolArgumentValidator.PrimaryArgument(tool.Descriptor.InputSchemaJson);
                string? matched = primary == null ? null : DenyPatternMatcher.FirstMatch(state.Binding.DenyPatterns, toolArguments.GetString(primary));
                if (matched != null)
                {
                    state.Record.Effect = EffectEnum.Destructive;
                    state.Record.EffectReason = "binding deny pattern " + matched;
                    return await DenyAsync(state, "binding_deny_pattern", "The " + primary + " argument matches a deny pattern on this resource binding; the call was rejected.", token).ConfigureAwait(false);
                }
            }

            // 5. Classify (settings only; secrets are resolved after any approval).
            EffectClassification classification;
            using (ConnectorContextLease settingsOnly = await _Contexts.BuildAsync(state.Resource, true, request.Principal, Writer(state), state.Binding?.AllowKnowledgeUpdates ?? true, token, false).ConfigureAwait(false))
            {
                ConnectorInvocationContext classifyContext = InvocationContext(state, settingsOnly.Context, null);
                try
                {
                    classification = tool.Classify(classifyContext, toolArguments) ?? new EffectClassification(EffectEnum.Destructive, "the classifier returned nothing", String.Empty);
                }
                catch (ConnectorException e)
                {
                    return await DenyAsync(state, e.Code, e.Message, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    classification = new EffectClassification(EffectEnum.Destructive, "the classifier failed (" + e.GetType().Name + "); treated as Destructive", tool.Descriptor.Name + " on " + state.Record.BindingAlias);
                }
            }
            state.Record.Effect = classification.Effect;
            state.Record.EffectReason = classification.Reason;
            state.Outcome.Effect = classification.Effect;
            state.Outcome.EffectReason = classification.Reason;

            // 6. Rules, ceiling, lockdown, matrix.
            AccessPolicyInput input = new AccessPolicyInput();
            input.Classification = classification;
            input.AccessMode = state.AccessMode;
            input.ResourceCeiling = state.Resource.DefaultAccessModeCeiling;
            input.RequireApprovalForDestructive = state.Binding?.RequireApprovalForDestructive ?? true;
            input.ToolRule = state.Binding?.ToolRules.FirstOrDefault(r => String.Equals(r.ToolName, tool.Descriptor.Name, StringComparison.Ordinal))?.Rule;
            input.TenantLockdown = tenant.Lockdown;
            AccessPolicyDecision policy = AccessPolicy.Decide(input);
            state.AccessMode = policy.EffectiveAccessMode;
            state.Record.AccessMode = policy.EffectiveAccessMode;
            state.Outcome.AccessMode = policy.EffectiveAccessMode;
            state.Outcome.ApprovalSummary = String.IsNullOrEmpty(classification.ApprovalSummary) ? tool.Descriptor.Name + " on " + state.Record.BindingAlias : classification.ApprovalSummary;

            if (policy.Decision == InvocationDecisionEnum.Allowed || policy.Decision == InvocationDecisionEnum.ApprovalRequired)
            {
                if (tool.Descriptor.RequiresModel && (state.Profile == null || String.IsNullOrEmpty(state.Profile.InferenceEndpointId)))
                    return await DenyAsync(state, "model_required", "Tool " + tool.Descriptor.Name + " needs the profile's inference endpoint, and profile " + (state.Profile?.Name ?? "(none)") + " has none.", token).ConfigureAwait(false);
            }

            if (request.DryRun)
            {
                state.Outcome.DryRun = true;
                state.Outcome.Decision = policy.Decision;
                state.Outcome.DecisionCode = policy.ReasonCode;
                state.Outcome.Reason = policy.Reason;
                state.Outcome.Status = InvocationStatusEnum.NotExecuted;
                return state.Outcome;
            }

            if (policy.Decision == InvocationDecisionEnum.Denied) return await DenyAsync(state, policy.ReasonCode, policy.Reason, token).ConfigureAwait(false);

            if (policy.Decision == InvocationDecisionEnum.ApprovalRequired)
            {
                return await RequireApprovalAsync(state, request, policy, classification, token).ConfigureAwait(false);
            }

            state.Record.Decision = InvocationDecisionEnum.Allowed;
            state.Record.DecisionCode = policy.ReasonCode;
            state.Record.DecisionReason = policy.Reason;
            state.Outcome.Decision = InvocationDecisionEnum.Allowed;
            state.Outcome.DecisionCode = policy.ReasonCode;
            state.Outcome.Reason = policy.Reason;
            return await ExecuteAsync(state, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Wait for an approval's call to finish and return its outcome (MCP hugo_await_approval, REST polling).
        /// </summary>
        /// <param name="principal">Caller (must be the requester or hold Read on Approval; the surface checks).</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="approvalId">Approval identifier.</param>
        /// <param name="maxWait">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome: ApprovalRequired while still pending, otherwise the final outcome.</returns>
        /// <exception cref="NotFoundException">Thrown when the approval does not exist in the tenant.</exception>
        public async Task<ToolInvocationOutcome> AwaitApprovalAsync(RequestContext principal, string tenantId, string approvalId, TimeSpan maxWait, CancellationToken token)
        {
            Approval approval = await _Approvals.ReadAsync(tenantId, approvalId, token).ConfigureAwait(false);
            // An observer: the original invocation (when still waiting inline) or the unattended runner executes the call.
            if (approval.Status == ApprovalStatusEnum.Pending) await _Approvals.WaitAsync(tenantId, approvalId, maxWait, token, false).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow + maxWait;
            while (true)
            {
                if (_Completed.TryGetValue(approvalId, out ToolInvocationOutcome? done)) return done;
                ToolInvocation? record = await _Database.ToolInvocations.ReadAsync(tenantId, approval.ToolInvocationId, token).ConfigureAwait(false);
                approval = await _Approvals.ReadAsync(tenantId, approvalId, token).ConfigureAwait(false);
                if (record != null && record.Status != InvocationStatusEnum.Pending && record.Status != InvocationStatusEnum.Running) return FromRecord(record, approval);
                if (DateTime.UtcNow >= deadline || approval.Status == ApprovalStatusEnum.Pending) return FromRecord(record ?? new ToolInvocation { Id = approval.ToolInvocationId, ToolName = approval.ToolName, Decision = InvocationDecisionEnum.ApprovalRequired, Status = InvocationStatusEnum.Pending }, approval);
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private InvocationState NewState(InvocationRequest request)
        {
            InvocationState state = new InvocationState();
            state.Principal = request.Principal ?? throw new ArgumentNullException(nameof(request.Principal));
            state.TenantId = request.TenantId;
            ToolInvocation record = state.Record;
            record.TenantId = request.TenantId;
            record.ConversationId = request.ConversationId;
            record.MessageId = request.MessageId;
            record.Surface = request.Surface;
            record.PrincipalType = request.Principal.PrincipalType;
            record.PrincipalId = request.Principal.PrincipalId;
            record.PrincipalName = request.Principal.PrincipalName;
            record.AgentProfileId = request.ProfileId;
            record.ToolName = request.ToolName ?? String.Empty;
            string raw = String.IsNullOrWhiteSpace(request.ArgumentsJson) ? "{}" : request.ArgumentsJson;
            string redacted = SecretScrubber.Shared.Scrub(raw) ?? String.Empty;
            record.ArgumentsRedacted = redacted.Length > 65536 ? redacted.Substring(0, 65536) : redacted;
            record.RedactionApplied = !String.Equals(raw, record.ArgumentsRedacted, StringComparison.Ordinal);
            record.CorrelationId = request.CorrelationId ?? request.Principal.RequestId;
            record.TraceId = Activity.Current?.TraceId.ToHexString();
            state.Outcome.ToolName = record.ToolName;
            state.Outcome.ProfileId = request.ProfileId;
            state.Outcome.DryRun = request.DryRun;
            state.Outcome.InvocationId = request.DryRun ? null : record.Id;
            return state;
        }

        private static JsonObject? ParseArguments(string? json)
        {
            try
            {
                return JsonNode.Parse(String.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<ToolInvocationOutcome> DenyAsync(InvocationState state, string code, string reason, CancellationToken token)
        {
            ToolInvocationOutcome outcome = state.Outcome;
            outcome.Decision = InvocationDecisionEnum.Denied;
            outcome.DecisionCode = code;
            outcome.Reason = reason;
            outcome.Status = InvocationStatusEnum.NotExecuted;
            outcome.Result = ToolResult.Error(code, reason);
            if (state.Outcome.DryRun) return outcome;
            ToolInvocation record = state.Record;
            record.Decision = InvocationDecisionEnum.Denied;
            record.DecisionCode = code;
            record.DecisionReason = Truncate(reason, 2000);
            record.Status = InvocationStatusEnum.NotExecuted;
            record.ErrorCode = code;
            record.ErrorMessage = Truncate(reason, 2000);
            if (state.RecordPersisted)
            {
                await _Database.ToolInvocations.UpdateAsync(record, token).ConfigureAwait(false);
            }
            else
            {
                record.CreatedUtc = DateTime.UtcNow;
                await _Database.ToolInvocations.CreateAsync(record, token).ConfigureAwait(false);
                state.RecordPersisted = true;
            }
            if (!String.IsNullOrEmpty(record.ApprovalId)) _Completed[record.ApprovalId] = outcome;
            Observe(state, 0);
            return outcome;
        }

        private async Task<ToolInvocationOutcome> RequireApprovalAsync(InvocationState state, InvocationRequest request, AccessPolicyDecision policy, EffectClassification classification, CancellationToken token)
        {
            ToolInvocation record = state.Record;
            record.Decision = InvocationDecisionEnum.ApprovalRequired;
            record.DecisionCode = policy.ReasonCode;
            record.DecisionReason = Truncate(policy.Reason, 2000);
            record.Status = InvocationStatusEnum.Pending;
            record.CreatedUtc = DateTime.UtcNow;
            await _Database.ToolInvocations.CreateAsync(record, token).ConfigureAwait(false);
            state.RecordPersisted = true;

            Approval approval = new Approval();
            approval.TenantId = state.TenantId;
            approval.ToolInvocationId = record.Id;
            approval.AgentProfileId = state.Profile?.Id;
            approval.BindingId = state.Binding?.Id;
            approval.BindingAlias = record.BindingAlias;
            approval.ResourceId = state.Resource!.Id;
            approval.ToolName = record.ToolName;
            approval.Surface = request.Surface;
            approval.RequestedByPrincipalType = request.Principal.PrincipalType;
            approval.RequestedByPrincipalId = request.Principal.PrincipalId;
            approval.RequestedByName = request.Principal.PrincipalName;
            approval.Summary = Truncate(state.Outcome.ApprovalSummary, 2000);
            approval.Effect = classification.Effect;
            approval.EffectReason = Truncate(policy.Reason, 2000);
            approval.ArgumentsRedacted = record.ArgumentsRedacted;
            _PendingArguments[approval.Id] = state.ArgumentsJson;
            await _Approvals.CreateAsync(approval, token).ConfigureAwait(false);
            record.ApprovalId = approval.Id;
            await _Database.ToolInvocations.UpdateAsync(record, token).ConfigureAwait(false);

            ToolInvocationOutcome outcome = state.Outcome;
            outcome.Decision = InvocationDecisionEnum.ApprovalRequired;
            outcome.DecisionCode = policy.ReasonCode;
            outcome.Reason = policy.Reason;
            outcome.ApprovalId = approval.Id;
            outcome.ApprovalStatus = ApprovalStatusEnum.Pending;
            outcome.ApprovalExpiresUtc = approval.ExpiresUtc;
            outcome.Status = InvocationStatusEnum.Pending;
            Observe(state, 0);

            if (request.WaitMode == ApprovalWaitModeEnum.NoWait)
            {
                outcome.Result = PendingResult(approval, request.Surface);
                return outcome;
            }

            long waitStarted = Stopwatch.GetTimestamp();
            if (request.ApprovalListener != null)
            {
                AgentApprovalRequest card = new AgentApprovalRequest();
                card.ApprovalId = approval.Id;
                card.Summary = approval.Summary;
                card.Effect = approval.Effect;
                card.Reason = approval.EffectReason;
                card.Resource = approval.BindingAlias;
                card.ToolName = approval.ToolName;
                card.RedactedArgumentsJson = approval.ArgumentsRedacted;
                card.ExpiresUtc = approval.ExpiresUtc;
                await request.ApprovalListener.ApprovalRequiredAsync(card, token).ConfigureAwait(false);
            }
            TimeSpan? maxWait = request.WaitMode == ApprovalWaitModeEnum.WaitWithTimeout ? (request.WaitTimeout ?? TimeSpan.FromSeconds(_Settings.Approvals.McpWaitSeconds)) : null;
            ApprovalStatusEnum status = await _Approvals.WaitAsync(state.TenantId, approval.Id, maxWait, token).ConfigureAwait(false);
            outcome.ApprovalWaitMs = (long)Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds;
            outcome.ApprovalStatus = status;
            if (status == ApprovalStatusEnum.Pending)
            {
                outcome.Result = PendingResult(approval, request.Surface);
                return outcome;
            }

            Approval decided = await _Approvals.ReadAsync(state.TenantId, approval.Id, token).ConfigureAwait(false);
            if (request.ApprovalListener != null)
            {
                AgentApprovalResolution resolution = new AgentApprovalResolution();
                resolution.ApprovalId = approval.Id;
                resolution.Decision = status == ApprovalStatusEnum.Approved ? AgentToolDecisionEnum.Approved : status == ApprovalStatusEnum.Expired ? AgentToolDecisionEnum.Expired : AgentToolDecisionEnum.Rejected;
                resolution.DecidedBy = decided.DecidedByName ?? decided.DecidedByUserId;
                resolution.Comment = decided.DecisionNote;
                await request.ApprovalListener.ApprovalResolvedAsync(resolution, token).ConfigureAwait(false);
            }
            if (status != ApprovalStatusEnum.Approved)
            {
                _PendingArguments.TryRemove(approval.Id, out string? _);
                return await FinalizeNotApprovedAsync(state, decided, token).ConfigureAwait(false);
            }
            if (!await _Database.ToolInvocations.TryTransitionAsync(state.TenantId, record.Id, InvocationStatusEnum.Pending, InvocationStatusEnum.Running, token).ConfigureAwait(false))
            {
                return await AwaitApprovalAsync(request.Principal, state.TenantId, approval.Id, TimeSpan.FromSeconds(Math.Max(1, _Settings.Connectors.DefaultToolTimeoutSeconds)), token).ConfigureAwait(false);
            }
            _PendingArguments.TryRemove(approval.Id, out string? _);
            record.Status = InvocationStatusEnum.Running;
            record.Decision = InvocationDecisionEnum.Approved;
            outcome.Decision = InvocationDecisionEnum.Approved;
            outcome.DecisionCode = "approved";
            outcome.Reason = "Approved by " + (decided.DecidedByName ?? "an approver") + ".";
            return await ExecuteAsync(state, token).ConfigureAwait(false);
        }

        private async Task OnUnattendedDecisionAsync(Approval approval, CancellationToken token)
        {
            ToolInvocation? record = await _Database.ToolInvocations.ReadAsync(approval.TenantId, approval.ToolInvocationId, token).ConfigureAwait(false);
            if (record == null || record.Status != InvocationStatusEnum.Pending) return;
            InvocationState state = new InvocationState();
            state.TenantId = approval.TenantId;
            state.Record = record;
            state.RecordPersisted = true;
            state.Principal = PrincipalOf(record);
            state.Outcome.InvocationId = record.Id;
            state.Outcome.ToolName = record.ToolName;
            state.Outcome.ProfileId = record.AgentProfileId;
            state.Outcome.BindingAlias = record.BindingAlias;
            state.Outcome.ResourceId = record.ResourceId;
            state.Outcome.ConnectorType = record.ConnectorType;
            state.Outcome.Effect = record.Effect;
            state.Outcome.EffectReason = record.EffectReason;
            state.Outcome.AccessMode = record.AccessMode;
            state.Outcome.ApprovalId = approval.Id;
            state.Outcome.ApprovalStatus = approval.Status;
            state.Outcome.ApprovalSummary = approval.Summary;

            if (approval.Status != ApprovalStatusEnum.Approved)
            {
                _PendingArguments.TryRemove(approval.Id, out string? _);
                ToolInvocationOutcome final = await FinalizeNotApprovedAsync(state, approval, token).ConfigureAwait(false);
                _Completed[approval.Id] = final;
                return;
            }
            if (!await _Database.ToolInvocations.TryTransitionAsync(approval.TenantId, record.Id, InvocationStatusEnum.Pending, InvocationStatusEnum.Running, token).ConfigureAwait(false)) return;
            record.Status = InvocationStatusEnum.Running;
            record.Decision = InvocationDecisionEnum.Approved;
            state.Outcome.Decision = InvocationDecisionEnum.Approved;
            state.Outcome.DecisionCode = "approved";
            state.Outcome.Reason = "Approved by " + (approval.DecidedByName ?? "an approver") + ".";

            string? arguments = null;
            if (_PendingArguments.TryRemove(approval.Id, out string? pending)) arguments = pending;
            else if (!record.RedactionApplied)
            {
                JsonObject? parsed = ParseArguments(record.ArgumentsRedacted);
                parsed?.Remove("resource");
                arguments = parsed?.ToJsonString();
            }
            if (arguments == null)
            {
                await FailResumeAsync(state, "approval_context_lost", "The call's arguments were not kept after a restart (they contained values that are redacted in the record); run it again.", token).ConfigureAwait(false);
                return;
            }
            state.ArgumentsJson = arguments;
            if (!String.IsNullOrEmpty(record.AgentProfileId)) state.Profile = await _Database.AgentProfiles.ReadAsync(approval.TenantId, record.AgentProfileId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(record.BindingId)) state.Binding = await _Database.Bindings.ReadAsync(approval.TenantId, record.BindingId, token).ConfigureAwait(false);
            state.Resource = String.IsNullOrEmpty(record.ResourceId) ? null : await _Database.Resources.ReadAsync(approval.TenantId, record.ResourceId, token).ConfigureAwait(false);
            state.Connector = state.Resource == null ? null : _Registry.Get(state.Resource.ConnectorType);
            state.Tool = state.Connector?.Tools.FirstOrDefault(t => String.Equals(t.Descriptor.Name, record.ToolName, StringComparison.Ordinal));
            state.AccessMode = record.AccessMode ?? AccessModeEnum.ReadWriteWithApproval;
            if (state.Resource == null || state.Tool == null || (!String.IsNullOrEmpty(record.BindingId) && state.Binding == null))
            {
                await FailResumeAsync(state, "binding_not_found", "The resource or binding was removed before the approved call could run.", token).ConfigureAwait(false);
                return;
            }
            ToolInvocationOutcome outcome = await ExecuteAsync(state, token).ConfigureAwait(false);
            _Completed[approval.Id] = outcome;
            PruneCompleted();
        }

        private async Task FailResumeAsync(InvocationState state, string code, string message, CancellationToken token)
        {
            state.Record.Status = InvocationStatusEnum.Failed;
            state.Record.ErrorCode = code;
            state.Record.ErrorMessage = message;
            await _Database.ToolInvocations.UpdateAsync(state.Record, token).ConfigureAwait(false);
            state.Outcome.Status = InvocationStatusEnum.Failed;
            state.Outcome.Result = ToolResult.Error(code, message);
            if (!String.IsNullOrEmpty(state.Record.ApprovalId)) _Completed[state.Record.ApprovalId] = state.Outcome;
            Observe(state, 0);
        }

        private async Task<ToolInvocationOutcome> FinalizeNotApprovedAsync(InvocationState state, Approval approval, CancellationToken token)
        {
            InvocationDecisionEnum decision = approval.Status == ApprovalStatusEnum.Rejected ? InvocationDecisionEnum.Rejected
                : approval.Status == ApprovalStatusEnum.Expired ? InvocationDecisionEnum.Expired : InvocationDecisionEnum.Cancelled;
            string code = decision.ToString().ToLowerInvariant();
            string reason = approval.Status == ApprovalStatusEnum.Rejected
                ? "The approval was rejected by " + (approval.DecidedByName ?? "an approver") + (String.IsNullOrEmpty(approval.DecisionNote) ? "." : ": " + approval.DecisionNote)
                : approval.Status == ApprovalStatusEnum.Expired ? "The approval expired before anyone decided it." : "The approval was cancelled" + (String.IsNullOrEmpty(approval.DecisionNote) ? "." : ": " + approval.DecisionNote);
            state.Record.Decision = decision;
            state.Record.DecisionCode = code;
            state.Record.DecisionReason = Truncate(reason, 2000);
            state.Record.Status = InvocationStatusEnum.NotExecuted;
            await _Database.ToolInvocations.UpdateAsync(state.Record, token).ConfigureAwait(false);
            state.Outcome.Decision = decision;
            state.Outcome.DecisionCode = code;
            state.Outcome.Reason = reason;
            state.Outcome.ApprovalStatus = approval.Status;
            state.Outcome.Status = InvocationStatusEnum.NotExecuted;
            state.Outcome.Result = ToolResult.Error(code, reason);
            Observe(state, 0);
            return state.Outcome;
        }

        private async Task<ToolInvocationOutcome> ExecuteAsync(InvocationState state, CancellationToken token)
        {
            Resource resource = state.Resource!;
            IConnector connector = state.Connector!;
            IConnectorTool tool = state.Tool!;
            ToolInvocation record = state.Record;
            ToolInvocationOutcome outcome = state.Outcome;
            ToolResult result;
            long started = Stopwatch.GetTimestamp();
            InvocationStatusEnum status;

            using (Activity? activity = HugoTracing.Start(connector.Descriptor.Type + " " + tool.Descriptor.Name))
            {
                activity?.SetTag("hugo.connector", connector.Descriptor.Type);
                activity?.SetTag("hugo.tool", tool.Descriptor.Name);
                activity?.SetTag("hugo.effect", record.Effect?.ToString());
                activity?.SetTag("hugo.decision", outcome.Decision.ToString());
                activity?.SetTag("hugo.resource_id", resource.Id);
                activity?.SetTag("hugo.binding_id", record.BindingId);
                activity?.SetTag("hugo.surface", record.Surface.ToString());

                ResourceGateLease gate = await _Pool.AcquireAsync(resource.Id, token).ConfigureAwait(false);
                ConnectorContextLease? lease = null;
                try
                {
                    // Approval and resource concurrency waits can outlive an operator's metadata policy change.
                    if (tool.Descriptor.UpdatesKnowledge)
                    {
                        Tenant? currentTenant = await _Database.Tenants.ReadAsync(state.TenantId, token).ConfigureAwait(false);
                        if (currentTenant == null)
                            return await DenyAsync(state, "tenant_not_found", "Tenant not found.", token).ConfigureAwait(false);
                        if (currentTenant.Lockdown)
                            return await DenyAsync(state, "tenant_lockdown", "The tenant is in lockdown; saved resource context updates are not allowed.", token).ConfigureAwait(false);
                        if (!String.IsNullOrEmpty(record.BindingId))
                        {
                            Binding? currentBinding = await _Database.Bindings.ReadAsync(state.TenantId, record.BindingId, token).ConfigureAwait(false);
                            if (currentBinding == null || !currentBinding.Enabled || !currentBinding.AllowKnowledgeUpdates
                                || !String.Equals(currentBinding.ResourceId, resource.Id, StringComparison.Ordinal)
                                || !String.Equals(currentBinding.AgentProfileId, record.AgentProfileId, StringComparison.Ordinal))
                                return await DenyAsync(state, ConnectorErrorCodes.PermissionDenied, "Saved resource context updates are disabled for this resource binding.", token).ConfigureAwait(false);
                            state.Binding = currentBinding;
                        }
                    }
                    lease = await _Contexts.BuildAsync(resource, true, state.Principal, Writer(state), state.Binding?.AllowKnowledgeUpdates ?? true, token).ConfigureAwait(false);
                    IConnectorModel? model = await ModelAsync(state, token).ConfigureAwait(false);
                    if (model != null)
                        model = new ResourceContextConnectorModel(model, lease.Context.Knowledge, state.Binding?.Alias ?? resource.Name, () => _Settings.Instructions.MaxKnowledgeCharsPerResource);
                    ConnectorInvocationContext context = InvocationContext(state, lease.Context, model);
                    ToolArguments arguments = new ToolArguments(state.ArgumentsJson);
                    using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(context.Timeout);
                        try
                        {
                            result = await tool.ExecuteAsync(context, arguments, timeout.Token).ConfigureAwait(false) ?? ToolResult.Error("connector_error", "The tool returned no result.");
                        }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        {
                            result = ToolResult.Error(ConnectorErrorCodes.Timeout, "The call did not finish within " + (int)context.Timeout.TotalSeconds + " seconds.");
                        }
                        catch (ConnectorException e)
                        {
                            result = ToolResult.Error(e.Code, e.Message);
                            result.RetryAfterSeconds = e.RetryAfterSeconds;
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            result = ToolResult.Error("connector_error", e.Message);
                        }
                    }
                    await lease.FlushAsync().ConfigureAwait(false);

                    // 10. Scrub with this call's secret values, then bound the output (backstop for connectors).
                    result.Text = lease.Scrubber.Scrub(result.Text) ?? String.Empty;
                    if (result.StructuredJson != null) result.StructuredJson = lease.Scrubber.Scrub(result.StructuredJson);
                    Bound(result, context.Limits);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    record.Status = InvocationStatusEnum.Cancelled;
                    record.ErrorCode = ConnectorErrorCodes.Cancelled;
                    record.ErrorMessage = "The caller cancelled the call.";
                    record.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    outcome.Status = InvocationStatusEnum.Cancelled;
                    await SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
                    HugoTracing.Fail(activity, "cancelled");
                    Observe(state, 0);
                    throw;
                }
                catch (ConnectorException e)
                {
                    result = ToolResult.Error(e.Code, (lease?.Scrubber ?? SecretScrubber.Shared).Scrub(e.Message) ?? String.Empty);
                }
                finally
                {
                    lease?.Dispose();
                    gate.Dispose();
                }

                status = !result.IsError ? InvocationStatusEnum.Succeeded : (result.ErrorCode == ConnectorErrorCodes.Timeout ? InvocationStatusEnum.TimedOut : InvocationStatusEnum.Failed);
                if (result.IsError) HugoTracing.Fail(activity, result.ErrorCode ?? "error");
                if (result.IsError && result.ErrorCode == ConnectorErrorCodes.HostKeyMismatch)
                {
                    await _Contexts.AuditHostKeyMismatchAsync(state.Principal, state.TenantId, record.ResourceId, record.BindingAlias, result.Text).ConfigureAwait(false);
                }
                else HugoTracing.Ok(activity);
            }

            long duration = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long bytes = Encoding.UTF8.GetByteCount(result.Text);
            record.Status = status;
            record.ErrorCode = result.IsError ? result.ErrorCode : null;
            record.ErrorMessage = result.IsError ? Truncate(result.Text, 2000) : null;
            record.OutputBytes = bytes;
            record.OutputTruncated = result.Truncated;
            record.OutputPreview = Truncate(result.Text, _PreviewLength);
            record.DurationMs = duration;
            outcome.Status = status;
            outcome.Result = result;
            outcome.DurationMs = duration;
            await SaveAsync(state, CancellationToken.None).ConfigureAwait(false);
            HugoMetrics.Observe(HugoMetricCatalog.ToolDuration, duration / 1000.0, "connector", connector.Descriptor.Type, "tool", tool.Descriptor.Name);
            HugoMetrics.Observe(HugoMetricCatalog.ToolOutputBytes, bytes, "connector", connector.Descriptor.Type);
            Observe(state, duration);
            return outcome;
        }

        private async Task SaveAsync(InvocationState state, CancellationToken token)
        {
            if (state.RecordPersisted)
            {
                await _Database.ToolInvocations.UpdateAsync(state.Record, token).ConfigureAwait(false);
                return;
            }
            state.Record.CreatedUtc = DateTime.UtcNow;
            await _Recorder.EnqueueAsync(state.Record, token).ConfigureAwait(false);
            state.RecordPersisted = true;
        }

        private ConnectorInvocationContext InvocationContext(InvocationState state, ConnectorResourceContext resourceContext, IConnectorModel? model)
        {
            ConnectorInvocationContext context = new ConnectorInvocationContext();
            context.Resource = resourceContext;
            context.BindingAlias = state.Record.BindingAlias ?? resourceContext.Name;
            context.AccessMode = state.AccessMode;
            OutputLimits limits = new OutputLimits();
            if (state.Binding?.MaxOutputBytes != null) limits.MaxBytes = Math.Min(limits.MaxBytes, state.Binding.MaxOutputBytes.Value);
            if (state.Binding?.MaxRows != null) limits.MaxRows = Math.Min(limits.MaxRows, state.Binding.MaxRows.Value);
            context.Limits = limits;
            int seconds;
            if (state.Tool != null && state.Tool.Descriptor.RequiresModel) seconds = _Settings.Connectors.ModelToolTimeoutSeconds;
            else
            {
                seconds = _Settings.Connectors.DefaultToolTimeoutSeconds;
                if (state.Profile != null) seconds = Math.Min(seconds, state.Profile.ToolTimeoutSeconds);
            }
            context.Timeout = TimeSpan.FromSeconds(Math.Max(1, seconds));
            context.Model = model;
            context.AllowKnowledgeUpdates = state.Binding?.AllowKnowledgeUpdates ?? true;
            context.PrincipalSummary = state.Principal.PrincipalType + " " + (state.Principal.PrincipalName ?? state.Principal.PrincipalId ?? "unknown");
            context.CorrelationId = state.Record.CorrelationId ?? state.Record.Id;
            return context;
        }

        private async Task<IConnectorModel?> ModelAsync(InvocationState state, CancellationToken token)
        {
            AgentProfile? profile = state.Profile;
            if (profile == null || String.IsNullOrEmpty(profile.InferenceEndpointId)) return null;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(state.TenantId, profile.InferenceEndpointId, token).ConfigureAwait(false);
            if (endpoint == null || !endpoint.Active) return null;
            ModelEndpointConfig primary = await _Models.ToConfigAsync(endpoint, token).ConfigureAwait(false);
            ModelEndpointConfig? fallback = null;
            if (!String.IsNullOrEmpty(profile.FallbackInferenceEndpointId))
            {
                ModelEndpoint? second = await _Database.ModelEndpoints.ReadAsync(state.TenantId, profile.FallbackInferenceEndpointId, token).ConfigureAwait(false);
                if (second != null && second.Active) fallback = await _Models.ToConfigAsync(second, token).ConfigureAwait(false);
            }
            return new ServerConnectorModel(_Models.Gateway, primary, fallback, _Settings.Connectors.ModelCallBudgetPerInvocation);
        }

        private static void Bound(ToolResult result, OutputLimits limits)
        {
            string text = result.Text ?? String.Empty;
            if (Encoding.UTF8.GetByteCount(text) > limits.MaxBytes) result.Text = OutputLimiter.Head(text, limits.MaxBytes, 0, result);
            if (result.StructuredJson != null && Encoding.UTF8.GetByteCount(result.StructuredJson) > limits.MaxBytes)
            {
                result.StructuredJson = null;
                result.Truncated = true;
            }
        }

        private static ToolResult PendingResult(Approval approval, InvocationSurfaceEnum surface)
        {
            string next = surface == InvocationSurfaceEnum.Mcp ? "call hugo_await_approval with this approvalId" : "GET /v1.0/api/approvals/" + approval.Id;
            string json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                { "status", "approval_pending" },
                { "approvalId", approval.Id },
                { "summary", approval.Summary },
                { "expiresUtc", approval.ExpiresUtc.ToString("o") },
                { "next", next }
            });
            ToolResult result = ToolResult.FromText("Approval " + approval.Id + " is pending: " + approval.Summary + ". It expires at " + approval.ExpiresUtc.ToString("u") + "; next: " + next + ".", json);
            result.MimeType = "application/json";
            return result;
        }

        private ToolInvocationOutcome FromRecord(ToolInvocation record, Approval approval)
        {
            ToolInvocationOutcome outcome = new ToolInvocationOutcome();
            outcome.InvocationId = record.Id;
            outcome.ToolName = record.ToolName;
            outcome.ProfileId = record.AgentProfileId;
            outcome.BindingAlias = record.BindingAlias;
            outcome.ResourceId = record.ResourceId;
            outcome.ConnectorType = record.ConnectorType;
            outcome.Decision = record.Decision;
            outcome.DecisionCode = record.DecisionCode;
            outcome.Reason = record.DecisionReason;
            outcome.Effect = record.Effect;
            outcome.EffectReason = record.EffectReason;
            outcome.AccessMode = record.AccessMode;
            outcome.ApprovalId = approval.Id;
            outcome.ApprovalStatus = approval.Status;
            outcome.ApprovalSummary = approval.Summary;
            outcome.ApprovalExpiresUtc = approval.ExpiresUtc;
            outcome.Status = record.Status;
            outcome.DurationMs = record.DurationMs;
            if (record.Status == InvocationStatusEnum.Pending) outcome.Result = PendingResult(approval, InvocationSurfaceEnum.Mcp);
            else if (record.Status == InvocationStatusEnum.Succeeded) outcome.Result = new ToolResult { Text = record.OutputPreview ?? String.Empty, Truncated = record.OutputTruncated || record.OutputBytes > (record.OutputPreview ?? String.Empty).Length };
            else outcome.Result = ToolResult.Error(record.ErrorCode ?? record.DecisionCode ?? "not_executed", record.ErrorMessage ?? record.DecisionReason ?? "The call did not run.");
            return outcome;
        }

        private void Observe(InvocationState state, long durationMs)
        {
            ToolInvocation record = state.Record;
            TagList tags = new TagList();
            tags.Add("connector", HugoMetrics.Fold("connector", record.ConnectorType ?? "unknown"));
            tags.Add("tool", HugoMetrics.Fold("tool", record.ToolName));
            tags.Add("effect", record.Effect?.ToString() ?? "unknown");
            tags.Add("decision", record.Decision.ToString());
            tags.Add("status", record.Status.ToString());
            tags.Add("surface", record.Surface.ToString());
            HugoMetrics.Count(HugoMetricCatalog.ToolInvocations, 1, tags);
            _Events?.Publish("tool.completed", record.TenantId, new
            {
                invocationId = record.Id,
                toolName = record.ToolName,
                resource = record.BindingAlias,
                agentProfileId = record.AgentProfileId,
                effect = record.Effect?.ToString(),
                decision = record.Decision.ToString(),
                decisionCode = record.DecisionCode,
                status = record.Status.ToString(),
                approvalId = record.ApprovalId,
                durationMs = durationMs,
                surface = record.Surface.ToString()
            });
        }

        private void PruneCompleted()
        {
            if (_Completed.Count < 512) return;
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, CompletedResultMinutes));
            foreach (KeyValuePair<string, ToolInvocationOutcome> entry in _Completed.ToList())
            {
                if (entry.Value.ApprovalExpiresUtc.HasValue && entry.Value.ApprovalExpiresUtc.Value < cutoff) _Completed.TryRemove(entry.Key, out ToolInvocationOutcome? _);
            }
        }

        private static string Writer(InvocationState state)
        {
            return (state.Principal.PrincipalName ?? state.Principal.PrincipalId ?? "unknown") + " via " + state.Record.ToolName;
        }

        private static RequestContext PrincipalOf(ToolInvocation record)
        {
            RequestContext ctx = new RequestContext();
            ctx.IsAuthenticated = true;
            ctx.TenantId = record.TenantId;
            ctx.PrincipalType = record.PrincipalType;
            ctx.PrincipalName = record.PrincipalName;
            if (record.PrincipalType == PrincipalTypeEnum.Credential) ctx.CredentialId = record.PrincipalId;
            else ctx.UserId = record.PrincipalId;
            return ctx;
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        #endregion
    }
}
