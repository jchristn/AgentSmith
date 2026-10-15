namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Server.Protocols;

    /// <summary>
    /// Runs A2A tasks (P7.14, P7.15). A task is one message sent to a profile, run as a turn of the conversation bound to
    /// its context. The turn runs in the background, so a caller can stream it, poll it, or come back for it; its state
    /// is persisted (D-027): working while the loop runs, input-required while an approval waits (it resumes on its own
    /// once a human decides), then completed, failed, or canceled. Thread-safe.
    /// </summary>
    public class A2ATaskService
    {
        #region Public-Members

        /// <summary>
        /// Change notifications.
        /// </summary>
        public A2ATaskSignals Signals
        {
            get
            {
                return _Signals;
            }
        }

        /// <summary>
        /// Tasks running on this server.
        /// </summary>
        public int RunningCount
        {
            get
            {
                return _Running.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ServerContext _Context;
        private readonly AgentTurnHost _Host;
        private readonly ConversationService _Conversations;
        private readonly ApprovalService _Approvals;
        private readonly A2ATaskSignals _Signals = new A2ATaskSignals();
        private readonly ConcurrentDictionary<string, Task> _Running = new ConcurrentDictionary<string, Task>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="host">Agent turn host.</param>
        /// <param name="conversations">Conversation service.</param>
        /// <param name="approvals">Approval service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public A2ATaskService(ServerContext context, AgentTurnHost host, ConversationService conversations, ApprovalService approvals)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Host = host ?? throw new ArgumentNullException(nameof(host));
            _Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
            _Approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a task: continue the conversation of the context (or start one), claim it, and run the turn in the
        /// background.
        /// </summary>
        /// <param name="ctx">Caller (the task's owner).</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profile">Agent profile (already authorized for Execute).</param>
        /// <param name="text">Message text.</param>
        /// <param name="contextId">A2A context identifier, or null for a new context.</param>
        /// <param name="messageId">Client message identifier, or null.</param>
        /// <param name="token">Cancellation token for the setup (the turn itself outlives the request).</param>
        /// <returns>The task (state Working).</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="A2AAccessException">Thrown when the context belongs to someone else (-32001) or a turn is running in it (-32007).</exception>
        /// <exception cref="Hugo.Core.Services.ValidationException">Thrown when the message is empty or the profile has no inference endpoint.</exception>
        public async Task<A2ATask> StartAsync(RequestContext ctx, string tenantId, AgentProfile profile, string text, string? contextId, string? messageId, CancellationToken token)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (contextId != null && contextId.Length > 128) throw new Hugo.Core.Services.ValidationException("contextId is longer than 128 characters.", new Dictionary<string, string> { { "contextId", "At most 128 characters." } });

            Conversation? conversation = null;
            bool created = false;
            if (!String.IsNullOrWhiteSpace(contextId))
            {
                conversation = await _Context.Database.Conversations.ReadByA2AContextAsync(tenantId, profile.Id, contextId, token).ConfigureAwait(false);
                if (conversation != null && !ConversationService.CanSee(ctx, conversation)) throw new A2AAccessException(-32001, "Context " + contextId + " not found.");
            }
            if (conversation == null)
            {
                conversation = ConversationService.New(ctx, tenantId, profile, ConversationSourceEnum.A2A);
                conversation.A2AContextId = String.IsNullOrWhiteSpace(contextId) ? conversation.Id : contextId;
                conversation = await _Context.Database.Conversations.CreateAsync(conversation, token).ConfigureAwait(false);
                created = true;
            }

            A2ATask task = new A2ATask();
            task.TenantId = tenantId;
            task.AgentProfileId = profile.Id;
            task.PrincipalType = ctx.PrincipalType;
            task.PrincipalId = ctx.PrincipalId;
            task.ConversationId = conversation.Id;
            task.ContextId = conversation.A2AContextId ?? conversation.Id;
            task.MessageId = messageId;
            task.State = A2ATaskStateEnum.Working;

            AgentTurnOptions options = new AgentTurnOptions();
            options.Principal = ctx;
            options.TenantId = tenantId;
            options.Profile = profile;
            options.Conversation = conversation;
            options.UserMessage = text ?? String.Empty;
            options.Surface = InvocationSurfaceEnum.A2A;
            options.WaitMode = ApprovalWaitModeEnum.Wait;
            options.Sinks.Add(new A2ATaskEventSink(this, tenantId, task.Id));

            AgentTurnPlan plan;
            try
            {
                plan = await _Host.PrepareAsync(options, token).ConfigureAwait(false);
            }
            catch (AgentTurnConflictException)
            {
                throw new A2AAccessException(-32007, "Another task is running in context " + task.ContextId + ". Wait for it to finish or cancel it.", "TurnInProgress");
            }
            catch (Exception)
            {
                if (created) await _Context.Database.Conversations.DeleteAsync(tenantId, conversation.Id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            try
            {
                await _Context.Database.A2ATasks.CreateAsync(task, token).ConfigureAwait(false);
            }
            catch
            {
                plan.Dispose();
                throw;
            }
            _Running[task.Id] = Task.Run(() => RunAsync(plan, task));
            return task;
        }

        /// <summary>
        /// Change a running task's state (approval waits and resumes). Terminal tasks are not changed.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="taskId">Task identifier.</param>
        /// <param name="state">New state.</param>
        /// <param name="message">Status message.</param>
        /// <param name="pendingApprovalId">Approval waited for, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task TransitionAsync(string tenantId, string taskId, A2ATaskStateEnum state, string? message, string? pendingApprovalId, CancellationToken token)
        {
            A2ATask? task = await _Context.Database.A2ATasks.ReadAsync(tenantId, taskId, token).ConfigureAwait(false);
            if (task == null || task.IsTerminal()) return;
            task.State = state;
            task.StatusMessage = message;
            task.PendingApprovalId = pendingApprovalId;
            await _Context.Database.A2ATasks.UpdateAsync(task, token).ConfigureAwait(false);
            _Signals.Notify(taskId);
        }

        /// <summary>
        /// Wait until a task satisfies a condition, or the wait ends.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="taskId">Task identifier.</param>
        /// <param name="done">Condition.</param>
        /// <param name="maxWait">Longest wait.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task as last read.</returns>
        /// <exception cref="Hugo.Core.Services.NotFoundException">Thrown when the task disappears.</exception>
        public async Task<A2ATask> WaitAsync(string tenantId, string taskId, Func<A2ATask, bool> done, TimeSpan maxWait, CancellationToken token)
        {
            if (done == null) throw new ArgumentNullException(nameof(done));
            DateTime deadline = DateTime.UtcNow + maxWait;
            while (true)
            {
                A2ATask task = await _Context.Database.A2ATasks.ReadAsync(tenantId, taskId, token).ConfigureAwait(false) ?? throw new Hugo.Core.Services.NotFoundException("Task " + taskId + " not found.");
                if (done(task) || DateTime.UtcNow >= deadline) return task;
                TimeSpan remaining = deadline - DateTime.UtcNow;
                await _Signals.WaitAsync(taskId, remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Cancel a task: stop its turn and cancel the approval it waits for.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="task">Task.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The task once it stopped (or after a short wait).</returns>
        /// <exception cref="A2AAccessException">Thrown when the task already finished (-32002).</exception>
        public async Task<A2ATask> CancelAsync(RequestContext ctx, A2ATask task, CancellationToken token)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.IsTerminal()) throw new A2AAccessException(-32002, "Task " + task.Id + " already finished (" + task.State + ").", "TaskNotCancelable");
            if (!String.IsNullOrEmpty(task.PendingApprovalId))
            {
                try
                {
                    await _Approvals.DecideAsync(ctx, task.TenantId, task.PendingApprovalId, ApprovalStatusEnum.Cancelled, "The A2A task was cancelled.", token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is Hugo.Core.Services.ConflictException || e is Hugo.Core.Services.NotFoundException)
                {
                }
            }
            bool signalled = _Host.Turns.Cancel(task.ConversationId);
            if (!signalled && !_Running.ContainsKey(task.Id))
            {
                // The task is not running here (for example the server restarted): settle it directly.
                task.State = A2ATaskStateEnum.Canceled;
                task.StatusMessage = "The task was cancelled.";
                task.PendingApprovalId = null;
                await _Context.Database.A2ATasks.UpdateAsync(task, token).ConfigureAwait(false);
                _Signals.Notify(task.Id);
                return task;
            }
            return await WaitAsync(task.TenantId, task.Id, t => t.IsTerminal(), TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mark tasks left unfinished by a previous process as failed (startup recovery).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tasks settled.</returns>
        public async Task<int> RecoverAsync(CancellationToken token)
        {
            List<A2ATask> open = await _Context.Database.A2ATasks.ListByStateAsync(new List<A2ATaskStateEnum> { A2ATaskStateEnum.Submitted, A2ATaskStateEnum.Working, A2ATaskStateEnum.InputRequired }, token).ConfigureAwait(false);
            int settled = 0;
            foreach (A2ATask task in open.Where(t => !_Running.ContainsKey(t.Id)))
            {
                task.State = A2ATaskStateEnum.Failed;
                task.StatusMessage = "The server restarted while the task was running. Send the message again to retry.";
                task.PendingApprovalId = null;
                await _Context.Database.A2ATasks.UpdateAsync(task, token).ConfigureAwait(false);
                settled++;
            }
            return settled;
        }

        /// <summary>
        /// Wait for running tasks to finish (shutdown; their turns are cancelled by the server's stopping token).
        /// </summary>
        /// <param name="maxWait">Longest wait.</param>
        /// <returns>Task.</returns>
        public async Task DrainAsync(TimeSpan maxWait)
        {
            Task[] running = _Running.Values.ToArray();
            if (running.Length == 0) return;
            await Task.WhenAny(Task.WhenAll(running), Task.Delay(maxWait)).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(AgentTurnPlan plan, A2ATask task)
        {
            AgentTurnResult? result = null;
            string? failure = null;
            try
            {
                result = await _Host.RunAsync(plan, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                failure = SecretScrubber.Shared.Scrub(e.Message);
                _Context.Logging.Warn("[A2A] task " + task.Id + " failed: " + failure);
            }
            finally
            {
                plan.Dispose();
            }

            try
            {
                A2ATask current = await _Context.Database.A2ATasks.ReadAsync(task.TenantId, task.Id, CancellationToken.None).ConfigureAwait(false) ?? task;
                current.PendingApprovalId = null;
                if (result == null)
                {
                    current.State = A2ATaskStateEnum.Failed;
                    current.StatusMessage = "The task failed: " + (failure ?? "unknown error") + ".";
                }
                else
                {
                    current.InputTokens = result.TokensIn;
                    current.OutputTokens = result.TokensOut;
                    current.ToolCallCount = result.ToolCalls.Count;
                    switch (result.FinishReason)
                    {
                        case AgentFinishReasonEnum.Completed:
                        case AgentFinishReasonEnum.MaxIterations:
                        case AgentFinishReasonEnum.ToolCallLimit:
                            current.State = A2ATaskStateEnum.Completed;
                            current.StatusMessage = result.FinalText;
                            A2ATaskArtifact artifact = new A2ATaskArtifact();
                            artifact.TaskId = current.Id;
                            artifact.TenantId = current.TenantId;
                            artifact.ArtifactId = current.Id + "-result";
                            artifact.Name = "result";
                            artifact.MimeType = "text/plain";
                            artifact.Content = result.FinalText;
                            await _Context.Database.A2ATasks.AddArtifactAsync(artifact, CancellationToken.None).ConfigureAwait(false);
                            break;
                        case AgentFinishReasonEnum.ApprovalPending:
                            current.State = A2ATaskStateEnum.InputRequired;
                            current.StatusMessage = result.FinalText;
                            current.PendingApprovalId = result.PendingApprovalId;
                            break;
                        case AgentFinishReasonEnum.Cancelled:
                            current.State = A2ATaskStateEnum.Canceled;
                            current.StatusMessage = "The task was cancelled.";
                            break;
                        default:
                            current.State = A2ATaskStateEnum.Failed;
                            current.StatusMessage = "The task failed: " + (SecretScrubber.Shared.Scrub(result.ErrorMessage) ?? result.ErrorCode ?? "unknown error") + ".";
                            break;
                    }
                }
                await _Context.Database.A2ATasks.UpdateAsync(current, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Context.Logging.Warn("[A2A] recording the end of task " + task.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
            }
            finally
            {
                _Running.TryRemove(task.Id, out Task? _);
                _Signals.Notify(task.Id);
            }
        }

        #endregion
    }
}
