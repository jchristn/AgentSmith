namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Tool invocation pipeline suites (P3.22): the access mode matrix through the real pipeline, tool rules, the
    /// resource ceiling, binding deny patterns, tenant lockdown, approvals (approve, reject, expire, cancel, waiting,
    /// self-approval), secret scrubbing, output limits, timeouts, cancellation, resolution errors, knowledge, and learned
    /// settings, on every configured provider, using the echo connector (P3.20).
    /// </summary>
    public static class PipelineSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "pipeline-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "matrix", "Access mode matrix for Read, Write, Destructive, and classified calls through the real pipeline (" + provider + ")", ct => MatrixAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "rules-ceiling-patterns", "Tool rules, resource ceiling, and binding deny patterns (" + provider + ")", ct => RulesAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "lockdown", "Lockdown denies non-Read calls, cancels pending approvals, and is audited (" + provider + ")", ct => LockdownAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "approvals", "Approve, reject, expire, cancel, waiting callers, double decisions, and self-approval (" + provider + ")", ct => ApprovalsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "scrub-limits-timeout-cancel", "Secret scrubbing, output limits, timeouts, and caller cancellation (" + provider + ")", ct => ScrubLimitsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "resolution-records", "Resolution errors, argument validation, synchronous Denied records, and the dry run (" + provider + ")", ct => ResolutionAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "knowledge", "Knowledge tools honor allowKnowledgeUpdates; knowledge routes round trip, scrub, version, and audit (" + provider + ")", ct => KnowledgeAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "learn-setting", "Learned settings persist and pinned host keys are audited as HostKeyPinned (" + provider + ")", ct => LearnAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Tool invocation pipeline (" + provider + ")", cases));
            }
            return suites;
        }

        #endregion

        #region Private-Methods

        private static async Task MatrixAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Dictionary<string, Binding> bindings = new Dictionary<string, Binding>();
                foreach (string name in new[] { "ro", "rwa", "rw", "rw-open" })
                {
                    Resource resource = await rig.ResourceAsync("echo-" + name, token).ConfigureAwait(false);
                    AccessModeEnum mode = name == "ro" ? AccessModeEnum.ReadOnly : name == "rwa" ? AccessModeEnum.ReadWriteWithApproval : AccessModeEnum.ReadWrite;
                    bindings[name] = await rig.BindAsync(rig.Hugo.Id, resource.Id, mode, token, r =>
                    {
                        if (name == "rw-open") r.RequireApprovalForDestructive = false;
                    }).ConfigureAwait(false);
                }

                // Expected decisions: rows are effects (Read, Write, Destructive), columns are ro, rwa, rw, rw-open.
                Dictionary<string, string[]> expected = new Dictionary<string, string[]>
                {
                    { "Read", new[] { "Allowed", "Allowed", "Allowed", "Allowed" } },
                    { "Write", new[] { "Denied", "ApprovalRequired", "Allowed", "Allowed" } },
                    { "Destructive", new[] { "Denied", "ApprovalRequired", "ApprovalRequired", "Allowed" } }
                };
                string[] columns = new[] { "ro", "rwa", "rw", "rw-open" };
                for (int c = 0; c < columns.Length; c++)
                {
                    string alias = "echo-" + columns[c];
                    foreach (KeyValuePair<string, string[]> row in expected)
                    {
                        string tool = row.Key == "Read" ? "echo_read" : row.Key == "Write" ? "echo_write" : "echo_delete";
                        ToolInvocationOutcome dry = await rig.InvokeAsync(tool, alias, new { text = "x" }, token, 200, null, null, true).ConfigureAwait(false);
                        Assert.Equal(row.Value[c], dry.Decision.ToString(), tool + " on " + alias + " (dry run)");
                        Assert.True(dry.InvocationId == null, "A dry run must not record.");
                        string command = row.Key == "Read" ? "read status" : row.Key == "Write" ? "write file" : "delete everything";
                        ToolInvocationOutcome classified = await rig.InvokeAsync("echo_classify", alias, new { command = command }, token, 200, null, null, true).ConfigureAwait(false);
                        Assert.Equal(row.Value[c], classified.Decision.ToString(), "echo_classify " + command + " on " + alias);
                        Assert.Equal(row.Key, classified.Effect?.ToString(), "classified effect of " + command);
                    }
                }

                ToolInvocationOutcome read = await rig.InvokeAsync("echo_read", "echo-ro", new { text = "hi" }, token, 200).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.Succeeded, read.Status, "read status");
                Assert.True(read.Result != null && read.Result.Text.StartsWith("hi greeting=hello", StringComparison.Ordinal), "read output: " + read.Result?.Text);
                ToolInvocationOutcome denied = await rig.InvokeAsync("echo_write", "echo-ro", new { text = "hi" }, token, 200).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Denied, denied.Decision, "write on ReadOnly");
                Assert.Equal("readonly_binding", denied.DecisionCode, "denial code");
                Assert.True(denied.Result != null && denied.Result.IsError, "denied result is an error");
                ToolInvocation? deniedRecord = await rig.Context.Database.ToolInvocations.ReadAsync(rig.TenantId, denied.InvocationId!, token).ConfigureAwait(false);
                Assert.True(deniedRecord != null && deniedRecord.Decision == InvocationDecisionEnum.Denied, "Denied rows are written synchronously.");
                ToolInvocationOutcome rejected = await rig.InvokeAsync("echo_classify", "echo-rw-open", new { command = "reject this" }, token, 200).ConfigureAwait(false);
                Assert.Equal("classifier_rejected", rejected.DecisionCode, "classifier rejection");
                ToolInvocationOutcome open = await rig.InvokeAsync("echo_delete", "echo-rw-open", new { text = "gone" }, token, 200).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.Succeeded, open.Status, "destructive with approval opt-out runs");
                ToolInvocation record = await rig.RecordAsync(open.InvocationId!, token).ConfigureAwait(false);
                Assert.Equal(EffectEnum.Destructive, record.Effect, "recorded effect");
                Assert.Equal(InvocationDecisionEnum.Allowed, record.Decision, "recorded decision");
                Assert.True(record.OutputPreview != null && record.OutputPreview.Contains("gone", StringComparison.Ordinal), "recorded preview");
            }
        }

        private static async Task RulesAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                Binding binding = await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadWrite, token, r =>
                {
                    r.ToolRules = new List<BindingToolRule>
                    {
                        new BindingToolRule { ToolName = "echo_read", Rule = ToolRuleEnum.Deny },
                        new BindingToolRule { ToolName = "echo_write", Rule = ToolRuleEnum.RequireApproval }
                    };
                    r.DenyPatterns = new List<string> { "rm\\s+-rf", "^write\\s+/etc" };
                }).ConfigureAwait(false);
                Assert.Equal(2, binding.ToolRules.Count, "tool rules stored");
                Assert.Equal(2, binding.DenyPatterns.Count, "deny patterns stored");

                ToolInvocationOutcome deniedRead = await rig.InvokeAsync("echo_read", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal("tool_denied", deniedRead.DecisionCode, "Deny rule");
                ToolInvocationOutcome approval = await rig.InvokeAsync("echo_write", "echo-a", null, token, 202).ConfigureAwait(false);
                Assert.Equal("tool_requires_approval", approval.DecisionCode, "RequireApproval rule");
                ToolInvocationOutcome pattern = await rig.InvokeAsync("echo_classify", "echo-a", new { command = "read; rm -rf /" }, token).ConfigureAwait(false);
                Assert.Equal("binding_deny_pattern", pattern.DecisionCode, "deny pattern on the primary argument");
                ToolInvocationOutcome anchored = await rig.InvokeAsync("echo_classify", "echo-a", new { command = "write /etc/passwd" }, token).ConfigureAwait(false);
                Assert.Equal("binding_deny_pattern", anchored.DecisionCode, "anchored deny pattern");
                ToolInvocationOutcome fine = await rig.InvokeAsync("echo_classify", "echo-a", new { command = "write /tmp/x" }, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Allowed, fine.Decision, "non-matching classified call");

                CallerToolCatalog tools = await HttpTestHelpers.SendAsync<CallerToolCatalog>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/tools", null, 200, token).ConfigureAwait(false);
                Assert.True(!tools.Tools.Any(t => t.Name == "echo_read"), "A tool denied on every binding is hidden.");
                Assert.True(tools.Tools.Any(t => t.Name == "echo_write"), "echo_write is listed.");

                TestResponse badPattern = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/bindings/" + binding.Id, new { denyPatterns = new[] { "([" } }, token).ConfigureAwait(false);
                Assert.Equal(400, badPattern.StatusCode, "invalid regex rejected");
                TestResponse badRule = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/bindings/" + binding.Id + "/tool-rules", new[] { new { toolName = "sql_query", rule = "Deny" } }, token).ConfigureAwait(false);
                Assert.Equal(400, badRule.StatusCode, "rule for a tool the connector lacks");

                Resource capped = await rig.ResourceAsync("echo-capped", token, "echo", null, null, AccessModeEnum.ReadOnly).ConfigureAwait(false);
                TestResponse over = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/bindings", new { resourceId = capped.Id, accessMode = "ReadWrite" }, token).ConfigureAwait(false);
                Assert.Equal(400, over.StatusCode, "binding above the ceiling is refused");
                Resource ceiling = await rig.ResourceAsync("echo-ceiling", token, "echo", null, null, AccessModeEnum.ReadWriteWithApproval).ConfigureAwait(false);
                Binding ceilingBinding = await rig.BindAsync(rig.Hugo.Id, ceiling.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                await rig.SqlAsync("UPDATE " + rig.Table("resources") + " SET default_access_mode_ceiling = @p0 WHERE id = @p1;", token, "ReadOnly", ceiling.Id).ConfigureAwait(false);
                ToolInvocationOutcome lowered = await rig.InvokeAsync("echo_write", "echo-ceiling", null, token).ConfigureAwait(false);
                Assert.Equal("readonly_binding", lowered.DecisionCode, "a lowered ceiling caps an existing binding");
                Assert.Equal(AccessModeEnum.ReadOnly, lowered.AccessMode, "effective access mode");
                Assert.True(ceilingBinding.AccessMode == AccessModeEnum.ReadWriteWithApproval, "binding unchanged");
            }
        }

        private static async Task LockdownAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                Resource b = await rig.ResourceAsync("echo-b", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadWrite, token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, b.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                ToolInvocationOutcome pending = await rig.InvokeAsync("echo_write", "echo-b", null, token, 202).ConfigureAwait(false);

                LockdownState on = await HttpTestHelpers.SendAsync<LockdownState>(rig.Anderson, HttpMethod.Post, "/v1.0/api/tenant/lockdown", new { reason = "incident" }, 200, token).ConfigureAwait(false);
                Assert.True(on.Lockdown, "lockdown on");
                Assert.Equal(1, on.ApprovalsCancelled, "pending approvals cancelled");
                Approval cancelled = await HttpTestHelpers.SendAsync<Approval>(rig.Anderson, HttpMethod.Get, "/v1.0/api/approvals/" + pending.ApprovalId, null, 200, token).ConfigureAwait(false);
                Assert.Equal(ApprovalStatusEnum.Cancelled, cancelled.Status, "approval cancelled by lockdown");
                Assert.True(cancelled.Invocation != null && cancelled.Invocation.Decision == InvocationDecisionEnum.Cancelled, "invocation marked cancelled");

                ToolInvocationOutcome write = await rig.InvokeAsync("echo_write", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal("tenant_lockdown", write.DecisionCode, "write denied in lockdown");
                ToolInvocationOutcome read = await rig.InvokeAsync("echo_read", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Allowed, read.Decision, "read allowed in lockdown");
                LockdownState state = await HttpTestHelpers.SendAsync<LockdownState>(rig.Anderson, HttpMethod.Get, "/v1.0/api/tenant/lockdown", null, 200, token).ConfigureAwait(false);
                Assert.True(state.Lockdown, "state reads back");

                LockdownState off = await HttpTestHelpers.SendAsync<LockdownState>(rig.Anderson, HttpMethod.Delete, "/v1.0/api/tenant/lockdown", null, 200, token).ConfigureAwait(false);
                Assert.True(!off.Lockdown, "lockdown off");
                ToolInvocationOutcome again = await rig.InvokeAsync("echo_write", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Allowed, again.Decision, "write allowed after lockdown");
                Assert.Equal(2, (await rig.AuditAsync(AuditEventTypeEnum.LockdownChanged, token).ConfigureAwait(false)).Count, "lockdown audited twice");
            }
        }

        private static async Task ApprovalsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);

                // Approve: nobody waits (REST), so the call runs when it is approved.
                ToolInvocationOutcome first = await rig.InvokeAsync("echo_write", "echo-a", new { text = "approved-run" }, token, 202).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.Pending, first.Status, "pending status");
                Assert.True(first.Result != null && !first.Result.IsError && first.Result.Text.Contains("approval", StringComparison.OrdinalIgnoreCase), "pending result explains the approval");
                TestResponse approved = await rig.DecideAsync(first.ApprovalId!, "approve", token).ConfigureAwait(false);
                Approval approval = Hugo.Core.Serialization.Serializer.Deserialize<Approval>(approved.Body)!;
                Assert.Equal(ApprovalStatusEnum.Approved, approval.Status, "approved");
                Assert.True(approval.Invocation != null && approval.Invocation.Status == InvocationStatusEnum.Succeeded, "the approved call ran: " + approved.Body);
                Assert.True(approval.Invocation!.OutputPreview!.Contains("approved-run", StringComparison.Ordinal), "result attached");
                Assert.Equal(InvocationDecisionEnum.Approved, approval.Invocation.Decision, "decision Approved");
                await rig.DecideAsync(first.ApprovalId!, "approve", token, 409).ConfigureAwait(false);
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.ApprovalDecided, token).ConfigureAwait(false)).Count >= 1, "decision audited");

                // Reject.
                ToolInvocationOutcome second = await rig.InvokeAsync("echo_write", "echo-a", null, token, 202).ConfigureAwait(false);
                await rig.DecideAsync(second.ApprovalId!, "reject", token).ConfigureAwait(false);
                ToolInvocation rejected = await rig.RecordAsync(second.InvocationId!, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Rejected, rejected.Decision, "rejected decision");
                Assert.Equal(InvocationStatusEnum.NotExecuted, rejected.Status, "rejected call not executed");

                // Cancel by the requester.
                ToolInvocationOutcome third = await rig.InvokeAsync("echo_write", "echo-a", null, token, 202).ConfigureAwait(false);
                await rig.DecideAsync(third.ApprovalId!, "cancel", token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Cancelled, (await rig.RecordAsync(third.InvocationId!, token).ConfigureAwait(false)).Decision, "cancelled decision");

                // Expire: move the expiry into the past and run the expiry job.
                ToolInvocationOutcome fourth = await rig.InvokeAsync("echo_write", "echo-a", null, token, 202).ConfigureAwait(false);
                await rig.SqlAsync("UPDATE " + rig.Table("approvals") + " SET expires_utc = @p0 WHERE id = @p1;", token, DateTime.UtcNow.AddMinutes(-1), fourth.ApprovalId).ConfigureAwait(false);
                int expired = await rig.Context.Get<ApprovalService>().ExpireDueAsync(token).ConfigureAwait(false);
                Assert.Equal(1, expired, "one approval expired");
                Approval expiredApproval = await rig.Context.Get<ApprovalService>().ReadAsync(rig.TenantId, fourth.ApprovalId!, token).ConfigureAwait(false);
                Assert.Equal(ApprovalStatusEnum.Expired, expiredApproval.Status, "expired status");
                Assert.Equal(InvocationDecisionEnum.Expired, (await rig.RecordAsync(fourth.InvocationId!, token).ConfigureAwait(false)).Decision, "expired decision");
                await rig.DecideAsync(fourth.ApprovalId!, "approve", token, 409).ConfigureAwait(false);

                // A waiting caller (chat, A2A) blocks until the decision, then the call runs inside its own request.
                ToolInvocationService pipeline = rig.Context.Get<ToolInvocationService>();
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                InvocationRequest request = new InvocationRequest();
                request.Principal = principal;
                request.TenantId = rig.TenantId;
                request.ProfileId = rig.Hugo.Id;
                request.ToolName = "echo_write";
                request.ArgumentsJson = "{\"resource\":\"echo-a\",\"text\":\"waited\"}";
                request.Surface = InvocationSurfaceEnum.Chat;
                request.WaitMode = ApprovalWaitModeEnum.Wait;
                Task<ToolInvocationOutcome> waiting = pipeline.InvokeAsync(request, token);
                Approval? pendingApproval = null;
                for (int i = 0; i < 100 && pendingApproval == null; i++)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                    List<Approval> pendingList = await rig.Context.Database.Approvals.ListPendingAsync(rig.TenantId, token).ConfigureAwait(false);
                    pendingApproval = pendingList.FirstOrDefault();
                }
                Assert.True(pendingApproval != null, "the waiting call created an approval");
                Assert.True(!waiting.IsCompleted, "the caller is still waiting");
                await rig.DecideAsync(pendingApproval!.Id, "approve", token).ConfigureAwait(false);
                ToolInvocationOutcome waited = await waiting.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.Approved, waited.Decision, "waiting call approved");
                Assert.True(waited.Result != null && waited.Result.Text.Contains("waited", StringComparison.Ordinal), "waiting caller received the result");

                // MCP style: wait a short while, then return the pending approval.
                request.WaitMode = ApprovalWaitModeEnum.WaitWithTimeout;
                request.WaitTimeout = TimeSpan.FromMilliseconds(300);
                ToolInvocationOutcome timedOut = await pipeline.InvokeAsync(request, token).ConfigureAwait(false);
                Assert.Equal(InvocationDecisionEnum.ApprovalRequired, timedOut.Decision, "still pending after the MCP wait");
                await rig.DecideAsync(timedOut.ApprovalId!, "approve", token).ConfigureAwait(false);
                ToolInvocationOutcome awaited = await pipeline.AwaitApprovalAsync(principal, rig.TenantId, timedOut.ApprovalId!, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.Succeeded, awaited.Status, "awaited result after the decision");
            }

            // Self-approval turned off: the requester cannot decide, another tenant administrator can.
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Approvals.AllowSelfApproval = false, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadWriteWithApproval, token).ConfigureAwait(false);
                ToolInvocationOutcome pending = await rig.InvokeAsync("echo_write", "echo-a", null, token, 202).ConfigureAwait(false);
                TestResponse self = await rig.DecideAsync(pending.ApprovalId!, "approve", token, 409).ConfigureAwait(false);
                Assert.True(self.Body.Contains("SelfApprovalNotAllowed", StringComparison.Ordinal), "self-approval refused");
                using (HttpClient admin = await rig.Host.ActivateSeededUserAsync("admin@hugo.local", token).ConfigureAwait(false))
                {
                    await rig.DecideAsync(pending.ApprovalId!, "approve", token, 200, admin).ConfigureAwait(false);
                }
            }
        }

        private static async Task ScrubLimitsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, s => s.Connectors.DefaultToolTimeoutSeconds = 1, token).ConfigureAwait(false))
            {
                string secretValue = "tok-SECRETVALUE-9f8e7d6c";
                Secret secret = await rig.SecretAsync("echo-token", SecretKindEnum.ApiToken, new Dictionary<string, string> { { "token", secretValue } }, token).ConfigureAwait(false);
                Resource a = await rig.ResourceAsync("echo-a", token, "echo", null, secret.Id).ConfigureAwait(false);
                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token, r => r.MaxOutputBytes = 1024).ConfigureAwait(false);

                ToolInvocationOutcome revealed = await rig.InvokeAsync("echo_read", "echo-a", new { text = "x", revealSecret = true }, token).ConfigureAwait(false);
                Assert.True(revealed.Result != null && !revealed.Result.Text.Contains(secretValue, StringComparison.Ordinal), "the secret value is scrubbed from output: " + revealed.Result?.Text);
                Assert.True(revealed.Result!.Text.Contains("[redacted", StringComparison.Ordinal), "a redaction marker replaces it");
                ToolInvocation record = await rig.RecordAsync(revealed.InvocationId!, token).ConfigureAwait(false);
                Assert.True(record.OutputPreview != null && !record.OutputPreview.Contains(secretValue, StringComparison.Ordinal), "the record is scrubbed");

                ToolInvocationOutcome big = await rig.InvokeAsync("echo_read", "echo-a", new { text = "0123456789", repeat = 5000 }, token).ConfigureAwait(false);
                Assert.True(big.Result != null && big.Result.Truncated, "large output is truncated");
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(big.Result!.Text) < 1300, "output bounded by the binding's MaxOutputBytes: " + big.Result.Text.Length);
                Assert.True((await rig.RecordAsync(big.InvocationId!, token).ConfigureAwait(false)).OutputTruncated, "truncation recorded");

                ToolInvocationOutcome slow = await rig.InvokeAsync("echo_read", "echo-a", new { text = "slow", delayMs = 5000 }, token).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.TimedOut, slow.Status, "timeout status");
                Assert.Equal(ConnectorErrorCodes.Timeout, slow.Result?.ErrorCode, "timeout code");

                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                InvocationRequest request = new InvocationRequest();
                request.Principal = principal;
                request.TenantId = rig.TenantId;
                request.ProfileId = rig.Hugo.Id;
                request.ToolName = "echo_read";
                request.ArgumentsJson = "{\"resource\":\"echo-a\",\"text\":\"cancel\",\"delayMs\":800}";
                request.Surface = InvocationSurfaceEnum.Chat;
                bool cancelled = false;
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(200);
                    try
                    {
                        await rig.Context.Get<ToolInvocationService>().InvokeAsync(request, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                }
                Assert.True(cancelled, "caller cancellation throws OperationCanceledException");
                await rig.Context.Get<ToolInvocationRecorder>().FlushAsync(token).ConfigureAwait(false);
                Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> cancelledRows = await rig.Context.Database.ToolInvocations.EnumerateAsync(rig.TenantId, new Hugo.Core.Enumeration.EnumerationQuery(), new Hugo.Core.Requests.ToolInvocationFilter { Status = InvocationStatusEnum.Cancelled }, token).ConfigureAwait(false);
                Assert.Equal(1L, cancelledRows.TotalRecords, "cancelled call recorded");
            }
        }

        private static async Task ResolutionAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                Binding binding = await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ToolInvocationOutcome unknownAlias = await rig.InvokeAsync("echo_read", "nope", null, token).ConfigureAwait(false);
                Assert.Equal("binding_not_found", unknownAlias.DecisionCode, "unknown alias");
                Assert.True(unknownAlias.Reason!.Contains("echo-a", StringComparison.Ordinal), "the error lists available aliases");
                ToolInvocationOutcome unknownTool = await rig.InvokeAsync("sql_query", "echo-a", new { sql = "select 1" }, token).ConfigureAwait(false);
                Assert.Equal("tool_not_found", unknownTool.DecisionCode, "tool of another connector");
                ToolInvocationOutcome invalid = await rig.InvokeAsync("echo_read", "echo-a", new { text = 5 }, token).ConfigureAwait(false);
                Assert.Equal(ConnectorErrorCodes.InvalidArgument, invalid.DecisionCode, "schema type");
                ToolInvocationOutcome extra = await rig.InvokeAsync("echo_read", "echo-a", new { surprise = "x" }, token).ConfigureAwait(false);
                Assert.Equal(ConnectorErrorCodes.InvalidArgument, extra.DecisionCode, "additionalProperties false");
                ToolInvocationOutcome missing = await rig.InvokeAsync("echo_classify", "echo-a", new { text = "x" }, token).ConfigureAwait(false);
                Assert.Equal(ConnectorErrorCodes.InvalidArgument, missing.DecisionCode, "required argument");
                foreach (ToolInvocationOutcome outcome in new[] { unknownAlias, unknownTool, invalid })
                {
                    ToolInvocation? row = await rig.Context.Database.ToolInvocations.ReadAsync(rig.TenantId, outcome.InvocationId!, token).ConfigureAwait(false);
                    Assert.True(row != null && row.Decision == InvocationDecisionEnum.Denied && row.Status == InvocationStatusEnum.NotExecuted, "denied call recorded synchronously");
                }

                await rig.UpdateBindingAsync(binding, r => r.Enabled = false, token).ConfigureAwait(false);
                ToolInvocationOutcome disabled = await rig.InvokeAsync("echo_read", "echo-a", null, token).ConfigureAwait(false);
                Assert.Equal("binding_not_found", disabled.DecisionCode, "disabled binding");
                await rig.UpdateBindingAsync(binding, r => r.Enabled = true, token).ConfigureAwait(false);

                ToolInvocationSummary summary = await HttpTestHelpers.SendAsync<ToolInvocationSummary>(rig.Anderson, HttpMethod.Get, "/v1.0/api/tool-invocations/summary", null, 200, token).ConfigureAwait(false);
                Assert.True(summary.Total >= 6, "summary counts the calls");
                Hugo.Core.Enumeration.EnumerationResult<ToolInvocation> list = await HttpTestHelpers.SendAsync<Hugo.Core.Enumeration.EnumerationResult<ToolInvocation>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/tool-invocations?decision=Denied&maxResults=100", null, 200, token).ConfigureAwait(false);
                Assert.True(list.Objects.Count >= 6 && list.Objects.All(o => o.Decision == InvocationDecisionEnum.Denied), "filter by decision");
                ToolInvocation one = await HttpTestHelpers.SendAsync<ToolInvocation>(rig.Anderson, HttpMethod.Get, "/v1.0/api/tool-invocations/" + unknownAlias.InvocationId, null, 200, token).ConfigureAwait(false);
                Assert.Equal(unknownAlias.InvocationId, one.Id, "read one");

                ToolInvocationOutcome admin = Hugo.Core.Serialization.Serializer.Deserialize<ToolInvocationOutcome>((await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + a.Id + "/invoke", new { toolName = "echo_write", arguments = new { text = "direct" } }, token).ConfigureAwait(false)).Body)!;
                Assert.Equal(InvocationDecisionEnum.ApprovalRequired, admin.Decision, "administrator invoke uses ReadWriteWithApproval without a ceiling");
            }
        }

        private static async Task KnowledgeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                Binding binding = await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                ToolInvocationOutcome stored = await rig.InvokeAsync("echo_knowledge", "echo-a", new { scope = "database", content = "Orders are partitioned by month. password=hunter2" }, token).ConfigureAwait(false);
                Assert.Equal(InvocationStatusEnum.Succeeded, stored.Status, "knowledge tool runs on a ReadOnly binding: " + stored.Result?.Text);
                List<ResourceKnowledge> entries = await HttpTestHelpers.SendAsync<List<ResourceKnowledge>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id + "/knowledge", null, 200, token).ConfigureAwait(false);
                Assert.Equal(1, entries.Count, "one entry");
                Assert.Equal(KnowledgeSourceEnum.Agent, entries[0].Source, "agent source");
                Assert.True(!entries[0].Content.Contains("hunter2", StringComparison.Ordinal), "content scrubbed on write");

                ResourceKnowledge edited = await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id + "/knowledge/database", new { content = "Operator says: orders are monthly." }, 200, token).ConfigureAwait(false);
                Assert.Equal(KnowledgeSourceEnum.Operator, edited.Source, "operator source");
                Assert.Equal(2, edited.Version, "version 2");
                ResourceKnowledge table = await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Put, "/v1.0/api/resources/" + a.Id + "/knowledge/table/public.orders", new { content = "Table context." }, 200, token).ConfigureAwait(false);
                Assert.Equal("public.orders", table.Key, "keyed entry");
                ResourceKnowledge root = await HttpTestHelpers.SendAsync<ResourceKnowledge>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id + "/knowledge/database/_root", null, 200, token).ConfigureAwait(false);
                Assert.Equal("Operator says: orders are monthly.", root.Content, "root entry read");
                List<ResourceKnowledgeVersion> versions = await HttpTestHelpers.SendAsync<List<ResourceKnowledgeVersion>>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id + "/knowledge/database/_root/versions", null, 200, token).ConfigureAwait(false);
                Assert.Equal(2, versions.Count, "two versions");
                TestResponse deleted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/resources/" + a.Id + "/knowledge/table/public.orders", null, token).ConfigureAwait(false);
                Assert.Equal(204, deleted.StatusCode, "delete entry");
                Assert.True((await rig.AuditAsync(AuditEventTypeEnum.KnowledgeChanged, token).ConfigureAwait(false)).Count >= 4, "every knowledge write is audited");

                Hugo.Core.Instructions.CompiledInstructions compiled = await HttpTestHelpers.SendAsync<Hugo.Core.Instructions.CompiledInstructions>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/compiled-instructions", null, 200, token).ConfigureAwait(false);
                Assert.True(compiled.Text.Contains("Operator says: orders are monthly.", StringComparison.Ordinal), "the root knowledge entry is in the resource section");

                await rig.UpdateBindingAsync(binding, r => r.AllowKnowledgeUpdates = false, token).ConfigureAwait(false);
                ToolInvocationOutcome refused = await rig.InvokeAsync("echo_knowledge", "echo-a", new { scope = "database", content = "x" }, token).ConfigureAwait(false);
                Assert.True(refused.Result != null && refused.Result.IsError && refused.Result.ErrorCode == ConnectorErrorCodes.PermissionDenied, "knowledge writes refused when the binding forbids them: " + refused.Result?.Text);
            }
        }

        private static async Task LearnAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Resource a = await rig.ResourceAsync("echo-a", token).ConfigureAwait(false);
                ResourceTestResponse test = await HttpTestHelpers.SendAsync<ResourceTestResponse>(rig.Anderson, HttpMethod.Post, "/v1.0/api/resources/" + a.Id + "/test", null, 200, token).ConfigureAwait(false);
                Assert.True(test.Success, "test succeeds");
                Assert.Equal("SHA256:echo-pinned", test.LearnedSettings["hostKeyFingerprint"], "learned on first use");
                Resource reloaded = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal("SHA256:echo-pinned", reloaded.SettingValues()["hostKeyFingerprint"], "pinned value persisted");
                Assert.True(reloaded.LastTestResult == true, "test result recorded");
                Assert.Equal(1, (await rig.AuditAsync(AuditEventTypeEnum.HostKeyPinned, token).ConfigureAwait(false)).Count, "HostKeyPinned audited");

                await rig.BindAsync(rig.Hugo.Id, a.Id, AccessModeEnum.ReadOnly, token).ConfigureAwait(false);
                await rig.InvokeAsync("echo_learn", "echo-a", new { fingerprint = "SHA256:rotated" }, token).ConfigureAwait(false);
                Resource after = await HttpTestHelpers.SendAsync<Resource>(rig.Anderson, HttpMethod.Get, "/v1.0/api/resources/" + a.Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal("SHA256:rotated", after.SettingValues()["hostKeyFingerprint"], "learned during a call");
                Assert.Equal(2, (await rig.AuditAsync(AuditEventTypeEnum.HostKeyPinned, token).ConfigureAwait(false)).Count, "second pin audited");
            }
        }

        private static async Task<RequestContext> PrincipalAsync(IntegrationRig rig, CancellationToken token)
        {
            User anderson = await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "anderson@hugo.local", token).ConfigureAwait(false) ?? throw new TestAssertionException("anderson missing");
            RequestContext ctx = new RequestContext();
            ctx.IsAuthenticated = true;
            ctx.TenantId = rig.TenantId;
            ctx.PrincipalType = PrincipalTypeEnum.User;
            ctx.UserId = anderson.Id;
            ctx.User = anderson;
            ctx.PrincipalName = anderson.Email;
            ctx.IsTenantAdmin = anderson.IsTenantAdmin;
            return ctx;
        }

        #endregion
    }
}
