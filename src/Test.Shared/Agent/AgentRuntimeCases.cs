namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Agent.Events;
    using Hugo.Core.Inference;
    using Test.Shared.Agent.StubLlm;
    using Touchstone.Core;

    /// <summary>
    /// Agent runtime cases (P6.21): full turns through the stub LLM over a real PolyPrompt OpenAI-compatible client.
    /// </summary>
    public static class AgentRuntimeCases
    {
        private const string Suite = "agent.runtime";

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(new TestCaseDescriptor(Suite, "multi-tool-turn", "Multi-tool turn with parallel reads, wrapped outputs, tokens, and persistence", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Executor.On("sql_query", new ScriptedToolBehavior { Output = "42 active connections", DelayMs = 200 });
                    rig.Executor.On("logs_search", new ScriptedToolBehavior { Output = "checkout timeout x17", DelayMs = 200 });
                    rig.Executor.On("ssh_exec", new ScriptedToolBehavior { Output = "load average: 9.1", Effect = EffectEnum.Read });
                    rig.Stub.Script.Then(
                        StubLlmResponse.ToolCallList(
                            new StubToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"select count(*) from pg_stat_activity\"}"),
                            new StubToolCall("logs_search", "{\"resource\":\"checkout-logs\",\"query\":\"checkout\"}")),
                        StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"uptime\"}"),
                        StubLlmResponse.Message("Mr. Anderson, checkout is slow because web-01 is overloaded."));

                    AgentTurnResult r = await rig.RunAsync("Agent Smith, why is checkout slow?", ct).ConfigureAwait(false);

                    AgentAssert.Equal(AgentFinishReasonEnum.Completed, r.FinishReason, "finish");
                    AgentAssert.Equal("Mr. Anderson, checkout is slow because web-01 is overloaded.", r.FinalText, "final text");
                    AgentAssert.Equal(3, r.Iterations, "iterations");
                    AgentAssert.Equal(3, r.ToolCalls.Count, "tool calls");
                    AgentAssert.True(r.ToolCalls[0].Parallel && r.ToolCalls[1].Parallel && !r.ToolCalls[2].Parallel, "reads ran in parallel, unknown effect ran alone");
                    AgentAssert.Equal(2, rig.Executor.PeakConcurrency, "peak concurrency");
                    AgentAssert.Equal("prod-db", r.ToolCalls[0].Resource, "resource");
                    AgentAssert.True(r.TokensIn > 0 && r.TokensOut > 0 && !r.TokensEstimated, "tokens");
                    AgentAssert.True(r.TimeToFirstTokenMs >= 0 && r.TimeToFirstTokenMs <= r.DurationMs, "ttft");

                    StubRecordedRequest third = rig.Stub.ChatRequests[2];
                    List<StubChatMessage> tools = third.Messages.Where(m => m.Role == "tool").ToList();
                    AgentAssert.Equal(3, tools.Count, "three tool results in context");
                    AgentAssert.True(tools[0].Content!.StartsWith("<tool_output resource=\"prod-db\" tool=\"sql_query\" truncated=\"false\">", StringComparison.Ordinal), "wrapped: " + tools[0].Content);
                    AgentAssert.True(third.Messages[0].Role == "system" && third.Messages[0].Content!.Contains("<tool_output>", StringComparison.Ordinal), "posture text in system prompt");
                    AgentAssert.True(third.Messages.Count(m => m.Role == "assistant" && m.ToolCalls != null && m.ToolCalls.Count > 0) == 2, "assistant tool-call messages replayed");

                    List<AgentMessage> stored = rig.Store.AllMessages("cnv_test");
                    AgentAssert.Equal(7, stored.Count, "user, call, 2 tools, call, tool, answer");
                    AgentAssert.True(stored[0].Role == AgentMessageRoleEnum.User && stored[6].Role == AgentMessageRoleEnum.Assistant, "order");
                    AgentAssert.True(stored[6].TokensIn > 0, "per-message tokens");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "event-ordering", "Events follow the D-022 order with increasing sequence numbers", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"select 1\"}"), StubLlmResponse.Message("Done, Mr. Anderson."));
                    AgentTurnResult r = await rig.RunAsync("check the db", ct).ConfigureAwait(false);
                    List<string> names = rig.Sink.Names.Where(n => n != "assistant.delta").ToList();
                    AgentAssert.Equal("turn.started,tool.proposed,tool.started,tool.completed,turn.completed", String.Join(",", names), "order without deltas");
                    List<AgentEvent> events = rig.Sink.Events;
                    for (int i = 0; i < events.Count; i++) AgentAssert.Equal((long)(i + 1), events[i].Sequence, "sequence " + i);
                    AgentAssert.True(events.All(e => e.TurnId == r.TurnId && e.ConversationId == "cnv_test"), "stamped");
                    int lastTool = rig.Sink.Names.LastIndexOf("tool.completed");
                    int firstDelta = rig.Sink.Names.IndexOf("assistant.delta");
                    AgentAssert.True(firstDelta > lastTool, "answer streams after tools");
                    ToolCompletedEvent done = rig.Sink.Of<ToolCompletedEvent>()[0];
                    AgentAssert.True(done.Decision == AgentToolDecisionEnum.Allowed && done.OutputPreview == "ok" && done.Resource == "prod-db", "completed event data");
                    TurnCompletedEvent completed = rig.Sink.Of<TurnCompletedEvent>()[0];
                    AgentAssert.True(completed.FinishReason == AgentFinishReasonEnum.Completed && completed.ToolCallCount == 1 && completed.AssistantMessageId != null, "turn.completed data");
                    AgentAssert.Equal("Done, Mr. Anderson.", String.Concat(rig.Sink.Of<AssistantDeltaEvent>().Select(d => d.Text)), "deltas reassemble the answer");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "denied-tool-fed-back", "A denied tool result is fed back and the model answers", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Executor.On("ssh_exec", new ScriptedToolBehavior { Decision = AgentToolDecisionEnum.Denied, Effect = EffectEnum.Destructive });
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"prod-db-host\",\"command\":\"rm -rf /tmp/x\"}"), StubLlmResponse.Message("I am not allowed to do that, Mr. Anderson."));
                    AgentTurnResult r = await rig.RunAsync("clean up tmp", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.Completed, r.FinishReason, "finish");
                    AgentAssert.Equal(AgentToolDecisionEnum.Denied, r.ToolCalls[0].Decision, "denied");
                    AgentAssert.Equal(EffectEnum.Destructive, r.ToolCalls[0].Effect, "effect");
                    StubChatMessage fed = rig.Stub.ChatRequests[1].Messages.Last(m => m.Role == "tool");
                    AgentAssert.True(fed.Content!.Contains("error=\"denied\"", StringComparison.Ordinal) && fed.Content.Contains("ReadOnly", StringComparison.Ordinal), "denial fed back: " + fed.Content);
                    AgentAssert.Equal("I am not allowed to do that, Mr. Anderson.", r.FinalText, "answer");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "approval-approved-mid-turn", "Approval required, then approved mid-turn; the call runs and the turn finishes", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    ScriptedToolBehavior restart = new ScriptedToolBehavior { Decision = AgentToolDecisionEnum.ApprovalRequired, Effect = EffectEnum.Destructive, Output = "nginx restarted", ApprovalId = "apr_restart" };
                    rig.Executor.On("ssh_exec", restart);
                    rig.Sink.OnEvent = e =>
                    {
                        if (e is ApprovalRequiredEvent)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(100).ConfigureAwait(false);
                                restart.ApprovalSignal.TrySetResult(AgentToolDecisionEnum.Approved);
                            });
                        }
                    };
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"systemctl restart nginx\"}"), StubLlmResponse.Message("nginx has been restarted, Mr. Anderson."));
                    AgentTurnResult r = await rig.RunAsync("restart nginx on web-01", ct).ConfigureAwait(false);

                    AgentAssert.Equal(AgentFinishReasonEnum.Completed, r.FinishReason, "finish");
                    AgentAssert.Equal(AgentToolDecisionEnum.Approved, r.ToolCalls[0].Decision, "approved");
                    AgentAssert.Equal("apr_restart", r.ToolCalls[0].ApprovalId, "approval id");
                    AgentAssert.True(r.ToolCalls[0].ApprovalWaitMs >= 80, "approval wait measured: " + r.ToolCalls[0].ApprovalWaitMs);
                    List<string> names = rig.Sink.Names;
                    int required = names.IndexOf("approval.required");
                    int resolved = names.IndexOf("approval.resolved");
                    int completed = names.IndexOf("tool.completed");
                    AgentAssert.True(required > names.IndexOf("tool.started") && resolved > required && completed > resolved, "approval events in order: " + String.Join(",", names));
                    ApprovalRequiredEvent card = rig.Sink.Of<ApprovalRequiredEvent>()[0];
                    AgentAssert.True(card.ApprovalId == "apr_restart" && card.Effect == EffectEnum.Destructive && card.Summary.Contains("web-01", StringComparison.Ordinal) && card.Resource == "web-01", "approval card data");
                    AgentAssert.Equal(AgentToolDecisionEnum.Approved, rig.Sink.Of<ApprovalResolvedEvent>()[0].Decision, "resolution");
                    AgentAssert.True(rig.Stub.ChatRequests[1].Messages.Last(m => m.Role == "tool").Content!.Contains("nginx restarted", StringComparison.Ordinal), "output fed back");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "approval-rejected", "Approval rejected: the call does not run and the model is told", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    ScriptedToolBehavior restart = new ScriptedToolBehavior { Decision = AgentToolDecisionEnum.ApprovalRequired, Effect = EffectEnum.Destructive };
                    rig.Executor.On("ssh_exec", restart);
                    rig.Sink.OnEvent = e =>
                    {
                        if (e is ApprovalRequiredEvent) restart.ApprovalSignal.TrySetResult(AgentToolDecisionEnum.Rejected);
                    };
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"reboot\"}"), StubLlmResponse.Message("Understood, I did not reboot web-01."));
                    AgentTurnResult r = await rig.RunAsync("reboot web-01", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentToolDecisionEnum.Rejected, r.ToolCalls[0].Decision, "rejected");
                    AgentAssert.True(r.ToolCalls[0].IsError && r.ToolCalls[0].ErrorCode == "approval_rejected", "error result");
                    AgentAssert.Equal(0, rig.Executor.PeakConcurrency, "never executed");
                    AgentAssert.True(rig.Stub.ChatRequests[1].Messages.Last(m => m.Role == "tool").Content!.Contains("approval_rejected", StringComparison.Ordinal), "rejection fed back");
                    AgentAssert.Equal("Understood, I did not reboot web-01.", r.FinalText, "answer");
                    AgentAssert.Equal(AgentToolDecisionEnum.Rejected, rig.Sink.Of<ApprovalResolvedEvent>()[0].Decision, "resolved event");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "approval-pending-ends-turn", "A surface that does not wait ends the turn with ApprovalPending", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Executor.On("ssh_exec", new ScriptedToolBehavior { Decision = AgentToolDecisionEnum.ApprovalRequired, WaitForApproval = false, ApprovalId = "apr_later", Effect = EffectEnum.Write });
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"touch x\"}"), StubLlmResponse.Message("should not be requested"));
                    AgentTurnResult r = await rig.RunAsync("touch a file", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.ApprovalPending, r.FinishReason, "finish");
                    AgentAssert.Equal("apr_later", r.PendingApprovalId, "pending id");
                    AgentAssert.True(r.FinalText.Contains("apr_later", StringComparison.Ordinal), "explains the pending approval");
                    AgentAssert.Equal(1, rig.Stub.ChatRequests.Count, "no further model calls");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "max-iterations-forces-answer", "The last iteration forces tool choice none and the turn ends with an answer", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Settings.MaxIterations = 3;
                    rig.Stub.Script.Then(
                        StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"select 1\"}"),
                        StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"select 2\"}"),
                        StubLlmResponse.Message("Here is what I found."));
                    AgentTurnResult r = await rig.RunAsync("keep digging", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.MaxIterations, r.FinishReason, "finish");
                    AgentAssert.Equal("Here is what I found.", r.FinalText, "answer");
                    List<StubRecordedRequest> requests = rig.Stub.ChatRequests;
                    AgentAssert.Equal(3, requests.Count, "three iterations");
                    AgentAssert.True(requests[0].ToolChoice == "auto" && requests[0].ToolNames.Count > 0, "tools offered first");
                    AgentAssert.True(requests[2].ToolChoice == "none" && requests[2].ToolNames.Count == 0, "final iteration forced: " + requests[2].ToolChoice);
                    AgentAssert.Equal(rig.Settings.FinalAnswerInstruction, requests[2].Messages.Last().Content, "final answer instruction");

                    rig.Stub.Script.Then(
                        StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\"}"),
                        StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\"}"),
                        StubLlmResponse.ToolCall("sql_query", "{\"resource\":\"prod-db\"}"));
                    AgentTurnResult stubborn = await rig.RunAsync("again", ct).ConfigureAwait(false);
                    AgentAssert.Equal(rig.Settings.EmptyAnswerText, stubborn.FinalText, "tool calls on the forced iteration are ignored");
                    AgentAssert.Equal(2, stubborn.ToolCalls.Count, "only two executed");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "tool-call-limit", "MaxToolCallsPerTurn stops extra calls and forces the answer", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Settings.MaxToolCallsPerTurn = 2;
                    rig.Stub.Script.Then(
                        StubLlmResponse.ToolCallList(
                            new StubToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"a\"}"),
                            new StubToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"b\"}"),
                            new StubToolCall("sql_query", "{\"resource\":\"prod-db\",\"query\":\"c\"}")),
                        StubLlmResponse.Message("Two queries were enough."));
                    AgentTurnResult r = await rig.RunAsync("run three queries", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.ToolCallLimit, r.FinishReason, "finish");
                    AgentAssert.Equal(2, rig.Executor.Calls.Count, "two executed");
                    AgentAssert.Equal(3, r.ToolCalls.Count, "three recorded");
                    AgentAssert.True(r.ToolCalls[2].ErrorCode == "tool_call_limit" && r.ToolCalls[2].Decision == AgentToolDecisionEnum.Denied, "third refused");
                    AgentAssert.Equal("none", rig.Stub.ChatRequests[1].ToolChoice, "forced final");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "compaction-persists-summary", "Compaction summarizes old turns via the model and persists the summary", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Settings.CompactionKeepRecentMessages = 2;
                    rig.Settings.CompactionThresholdRatio = 0.5;
                    List<AgentMessage> seed = new List<AgentMessage>();
                    for (int i = 0; i < 10; i++)
                    {
                        seed.Add(new AgentMessage
                        {
                            Id = "msg_seed_" + i,
                            ConversationId = "cnv_test",
                            Role = i % 2 == 0 ? AgentMessageRoleEnum.User : AgentMessageRoleEnum.Assistant,
                            Content = "turn " + i + " " + new string('x', 600)
                        });
                    }
                    rig.Store.Seed("cnv_test", seed);
                    rig.Stub.Script.Then(StubLlmResponse.Message("SUMMARY: the operator investigated checkout latency."), StubLlmResponse.Message("Continuing from the summary."));
                    AgentTurnRequest request = rig.Request("what next?");
                    request.InferenceEndpoint!.ContextWindowTokens = 2000;
                    AgentTurnResult r = await rig.Runtime.RunTurnAsync(request, ct).ConfigureAwait(false);

                    AgentAssert.True(r.Compacted, "compacted");
                    AgentAssert.Equal("Continuing from the summary.", r.FinalText, "answer");
                    List<AgentCompaction> compactions = rig.Store.Compactions("cnv_test");
                    AgentAssert.Equal(1, compactions.Count, "one compaction persisted");
                    AgentAssert.Equal(8, compactions[0].CoveredMessageIds.Count, "covered all but the two most recent");
                    AgentAssert.Equal(AgentMessageRoleEnum.Summary, compactions[0].Summary.Role, "summary role");
                    StubRecordedRequest summarize = rig.Stub.ChatRequests[0];
                    AgentAssert.True(!summarize.Stream && summarize.ToolNames.Count == 0 && summarize.Messages[1].Content!.Contains("turn 0", StringComparison.Ordinal), "summary call");
                    StubRecordedRequest main = rig.Stub.ChatRequests[1];
                    AgentAssert.True(main.Messages[0].Content!.Contains("SUMMARY: the operator investigated checkout latency.", StringComparison.Ordinal), "summary in system prompt");
                    AgentAssert.Equal(4, main.Messages.Count, "system, two kept, user");
                    List<AgentMessage> history = await rig.Store.LoadHistoryAsync("cnv_test", ct).ConfigureAwait(false);
                    AgentAssert.True(history[0].Role == AgentMessageRoleEnum.Summary && history.Count == 5, "history after compaction: summary, 2 kept, user, answer");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "cancellation-mid-turn", "Cancellation stops the loop, cancels the in-flight tool, and records Cancelled", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    StandardCatalog(rig);
                    rig.Executor.On("ssh_exec", new ScriptedToolBehavior { DelayMs = 20000 });
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("ssh_exec", "{\"resource\":\"web-01\",\"command\":\"sleep 20\"}"), StubLlmResponse.Message("never"));
                    Stopwatch sw = Stopwatch.StartNew();
                    Task<AgentTurnResult> running = rig.Runtime.RunTurnAsync(rig.Request("run the long thing"), cts.Token);
                    await rig.Executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    cts.Cancel();
                    AgentTurnResult r = await running.ConfigureAwait(false);
                    sw.Stop();
                    AgentAssert.Equal(AgentFinishReasonEnum.Cancelled, r.FinishReason, "finish");
                    AgentAssert.True(sw.ElapsedMilliseconds < 10000, "stopped promptly");
                    AgentAssert.Equal(1, rig.Executor.CancelledInFlight, "in-flight tool cancelled");
                    AgentAssert.Equal(1, rig.Stub.ChatRequests.Count, "no further model calls");
                    AgentAssert.Equal("turn.completed", rig.Sink.Names.Last(), "turn.completed is last");
                    AgentAssert.Equal(AgentFinishReasonEnum.Cancelled, rig.Sink.Of<TurnCompletedEvent>()[0].FinishReason, "event finish");
                    List<AgentMessage> stored = rig.Store.AllMessages("cnv_test");
                    AgentAssert.True(stored.Any(m => m.Role == AgentMessageRoleEnum.Tool && m.Content.Contains("error=\"cancelled\"", StringComparison.Ordinal)), "dangling call closed and persisted");
                    AgentAssert.True(!rig.Runtime.Locks.IsHeld("cnv_test"), "lock released");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "tool-retrieval", "Tool retrieval picks relevant tools with the hashing embedder and caches tool vectors", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    for (int i = 0; i < 45; i++) rig.Catalog.Add("widget_op" + i, "Adjust widget gadget number " + i + " in the warehouse inventory.", EffectEnum.Write, "warehouse");
                    rig.Catalog.Add("sql_query", "Run a SQL query against a relational database and return rows.", null, "prod-db");
                    rig.Catalog.Add("ssh_exec", "Run a shell command on a remote host over SSH.", null, "web-01");
                    rig.Catalog.AddMeta("hugo_list_resources", "List the resources bound to this profile.");
                    rig.Settings.ToolRetrievalThreshold = 40;
                    rig.Settings.ToolRetrievalTopK = 3;
                    rig.Stub.Script.Then(StubLlmResponse.Message("ok"), StubLlmResponse.Message("ok again"));

                    AgentTurnRequest request = rig.Request("Run a SQL query on the database to count orders");
                    request.EmbeddingEndpoint = rig.Stub.EmbeddingEndpoint();
                    AgentTurnResult r = await rig.Runtime.RunTurnAsync(request, ct).ConfigureAwait(false);
                    AgentAssert.True(r.ToolRetrievalUsed, "retrieval used");
                    AgentAssert.Equal(48, r.ToolsAvailable, "available");
                    AgentAssert.Equal(4, r.ToolsOffered, "meta plus top 3");
                    List<string> offered = rig.Stub.ChatRequests[0].ToolNames;
                    AgentAssert.True(offered[0] == "hugo_list_resources" && offered[1] == "sql_query", "meta first, then the best match: " + String.Join(",", offered));
                    AgentAssert.Equal(48, rig.Stub.EmbeddingRequests[0].Inputs.Count, "query plus 47 tool descriptions");

                    AgentTurnRequest again = rig.Request("Run another SQL query", "cnv_other");
                    again.EmbeddingEndpoint = rig.Stub.EmbeddingEndpoint();
                    await rig.Runtime.RunTurnAsync(again, ct).ConfigureAwait(false);
                    AgentAssert.Equal(1, rig.Stub.EmbeddingRequests[1].Inputs.Count, "tool vectors cached");

                    AgentTurnRequest none = rig.Request("Run a SQL query", "cnv_third");
                    rig.Stub.Script.Then(StubLlmResponse.Message("all tools"));
                    AgentTurnResult all = await rig.Runtime.RunTurnAsync(none, ct).ConfigureAwait(false);
                    AgentAssert.True(!all.ToolRetrievalUsed && all.ToolsOffered == 48, "no embedding endpoint offers every tool");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "think-blocks", "Think blocks stream as assistant.thinking and are removed from the answer", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StubLlmResponse response = StubLlmResponse.Message("<think>The operator wants a status.</think>All systems nominal, Mr. Anderson.");
                    response.ChunkSize = 3;
                    response.Reasoning = "Provider reasoning.";
                    rig.Stub.Script.Then(response);
                    AgentTurnResult r = await rig.RunAsync("status?", ct).ConfigureAwait(false);
                    AgentAssert.Equal("All systems nominal, Mr. Anderson.", r.FinalText, "answer without think block");
                    string thinking = String.Concat(rig.Sink.Of<AssistantThinkingEvent>().Select(e => e.Text));
                    AgentAssert.True(thinking.Contains("The operator wants a status.", StringComparison.Ordinal) && thinking.Contains("Provider reasoning.", StringComparison.Ordinal), "thinking events: " + thinking);
                    AgentAssert.Equal("All systems nominal, Mr. Anderson.", String.Concat(rig.Sink.Of<AssistantDeltaEvent>().Select(e => e.Text)), "deltas carry no reasoning");
                    AgentAssert.True(r.Thinking != null && r.Thinking.Contains("The operator wants a status.", StringComparison.Ordinal), "thinking on result");
                    AgentAssert.True(rig.Store.AllMessages("cnv_test").Last().Thinking != null, "thinking persisted on the message");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "concurrent-turn-rejected", "A second turn on a busy conversation is rejected (409 path)", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    rig.Stub.Script.Then(StubLlmResponse.Message("slow").WithDelay(800), StubLlmResponse.Message("other"));
                    Task<AgentTurnResult> first = rig.RunAsync("first", ct);
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    await AgentAssert.ThrowsAsync<AgentTurnConflictException>(() => rig.RunAsync("second", ct), "conflict").ConfigureAwait(false);
                    AgentTurnResult other = await rig.Runtime.RunTurnAsync(rig.Request("parallel conversation", "cnv_parallel"), ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.Completed, other.FinishReason, "other conversation unaffected");
                    AgentTurnResult r = await first.ConfigureAwait(false);
                    AgentAssert.Equal("slow", r.FinalText, "first finished");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "fallback-endpoint-turn", "A turn completes on the fallback endpoint when the primary fails", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                using (StubLlmServer broken = new StubLlmServer())
                {
                    broken.Script.Responder = req => StubLlmResponse.Error(503);
                    rig.Stub.Script.Then(StubLlmResponse.Message("Served by the fallback."));
                    AgentTurnRequest request = rig.Request("hello");
                    request.FallbackEndpoint = request.InferenceEndpoint;
                    request.InferenceEndpoint = broken.InferenceEndpoint();
                    AgentTurnResult r = await rig.Runtime.RunTurnAsync(request, ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.Completed, r.FinishReason, "finish");
                    AgentAssert.True(r.UsedFallback, "fallback used");
                    AgentAssert.Equal("Served by the fallback.", r.FinalText, "answer");
                    AgentAssert.True(rig.Sink.Of<TurnCompletedEvent>()[0].UsedFallback, "event flag");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "model-failure-ends-turn", "When every endpoint fails the turn ends with Error and the user message is kept", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    rig.Stub.Script.Responder = req => StubLlmResponse.Error(500);
                    AgentTurnResult r = await rig.RunAsync("hello?", ct).ConfigureAwait(false);
                    AgentAssert.Equal(AgentFinishReasonEnum.Error, r.FinishReason, "finish");
                    AgentAssert.Equal("model_unavailable", r.ErrorCode, "code");
                    AgentErrorEvent error = rig.Sink.Of<AgentErrorEvent>()[0];
                    AgentAssert.True(error.Fatal && !error.Message.Contains("stub-key", StringComparison.Ordinal), "fatal error event without secrets");
                    AgentAssert.Equal("turn.completed", rig.Sink.Names.Last(), "turn.completed last");
                    AgentAssert.Equal(1, rig.Store.AllMessages("cnv_test").Count, "user message persisted");
                }
            }));

            cases.Add(new TestCaseDescriptor(Suite, "non-streaming-and-unknown-tool", "Non-streaming endpoints work and unknown tools are refused without execution", async ct =>
            {
                using (AgentTestRig rig = new AgentTestRig())
                {
                    StandardCatalog(rig);
                    rig.Stub.Script.Then(StubLlmResponse.ToolCall("rm_everything", "{}"), StubLlmResponse.Message("That tool does not exist."));
                    AgentTurnRequest request = rig.Request("delete it all");
                    request.InferenceEndpoint!.SupportsStreaming = false;
                    AgentTurnResult r = await rig.Runtime.RunTurnAsync(request, ct).ConfigureAwait(false);
                    AgentAssert.True(rig.Stub.ChatRequests.All(q => !q.Stream), "non-streaming requests");
                    AgentAssert.Equal("unknown_tool", r.ToolCalls[0].ErrorCode, "unknown tool refused");
                    AgentAssert.Equal(0, rig.Executor.Calls.Count, "executor not called");
                    AgentAssert.Equal("That tool does not exist.", r.FinalText, "answer");
                    AgentAssert.Equal(1, rig.Sink.Of<AssistantDeltaEvent>().Count, "single delta for a non-streamed answer");
                }
            }));

            return cases;
        }

        private static void StandardCatalog(AgentTestRig rig)
        {
            rig.Catalog.Add("sql_query", "Run a read-only SQL query against a database.", EffectEnum.Read, "prod-db");
            rig.Catalog.Add("logs_search", "Search SolarWinds checkout logs.", EffectEnum.Read, "checkout-logs");
            rig.Catalog.Add("ssh_exec", "Run a shell command on a host over SSH.", null, "web-01");
            rig.Catalog.AddMeta("hugo_list_resources", "List the resources bound to this profile.");
        }
    }
}
