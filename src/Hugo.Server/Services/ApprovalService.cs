namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Services;
    using Hugo.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Approvals (P3.15, plan section 11.4). Approvals are database rows (D-027); a decision is an atomic transition out
    /// of Pending, so concurrent approvers, the expiry job, and lockdown cannot both win. Callers that wait (chat, A2A, MCP
    /// for a while) block in <see cref="WaitAsync"/>, which wakes on an in-process signal and also polls the database, so
    /// a decision made on another replica is seen too. When nobody is waiting, a decision is handed to
    /// <see cref="Unattended"/> (the tool invocation service resumes or finalizes the call). Every decision is audited
    /// (ApprovalDecided), counted, and published as approval.* events. Thread-safe.
    /// </summary>
    public class ApprovalService
    {
        #region Public-Members

        /// <summary>
        /// Seconds before a new approval expires. Read on every create. Default 900.
        /// </summary>
        public Func<int> ExpirySeconds
        {
            get
            {
                return _ExpirySeconds;
            }
            set
            {
                _ExpirySeconds = value ?? (() => 900);
            }
        }

        /// <summary>
        /// Whether a requester may approve or reject their own request. Read on every decision. Default true.
        /// </summary>
        public Func<bool> AllowSelfApproval
        {
            get
            {
                return _AllowSelfApproval;
            }
            set
            {
                _AllowSelfApproval = value ?? (() => true);
            }
        }

        /// <summary>
        /// Seconds between expiry sweeps. Default 5, minimum 1, maximum 3600.
        /// </summary>
        public int SweepIntervalSeconds
        {
            get
            {
                return _SweepIntervalSeconds;
            }
            set
            {
                _SweepIntervalSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Milliseconds between database checks while waiting (decisions made on another replica). Default 1000,
        /// minimum 50, maximum 60000.
        /// </summary>
        public int PollIntervalMs
        {
            get
            {
                return _PollIntervalMs;
            }
            set
            {
                _PollIntervalMs = Math.Clamp(value, 50, 60000);
            }
        }

        /// <summary>
        /// Called with a decided approval when no caller is waiting for it (set by the tool invocation service).
        /// </summary>
        public Func<Approval, CancellationToken, Task>? Unattended { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuditService _Audit;
        private readonly EventHub? _Events;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalStatusEnum>> _Waiters = new ConcurrentDictionary<string, TaskCompletionSource<ApprovalStatusEnum>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>> _Observers = new ConcurrentDictionary<string, ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>>(StringComparer.Ordinal);
        private Func<int> _ExpirySeconds = () => 900;
        private Func<bool> _AllowSelfApproval = () => true;
        private int _SweepIntervalSeconds = 5;
        private int _PollIntervalMs = 1000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="events">Event hub, or null.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ApprovalService(DatabaseDriverBase database, AuditService audit, EventHub? events, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Events = events;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Store a new pending approval.
        /// </summary>
        /// <param name="approval">Approval (tenant, invocation, summary, effect, requester, and so on).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored approval with its expiry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when approval is null.</exception>
        public async Task<Approval> CreateAsync(Approval approval, CancellationToken token = default)
        {
            if (approval == null) throw new ArgumentNullException(nameof(approval));
            approval.Status = ApprovalStatusEnum.Pending;
            approval.ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(_ExpirySeconds(), 30, 604800));
            await _Database.Approvals.CreateAsync(approval, token).ConfigureAwait(false);
            HugoMetrics.Count(HugoMetricCatalog.Approvals, 1, HugoMetrics.Tags("outcome", "requested"));
            _Events?.Publish("approval.required", approval.TenantId, Card(approval));
            return approval;
        }

        /// <summary>
        /// Read an approval.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Approval identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approval.</returns>
        /// <exception cref="NotFoundException">Thrown when it does not exist in the tenant.</exception>
        public async Task<Approval> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            return await _Database.Approvals.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Approval not found.");
        }

        /// <summary>
        /// Whether a caller in this process is waiting for an approval and will run the approved call itself.
        /// Observers (hugo_await_approval, REST polling) do not count.
        /// </summary>
        /// <param name="approvalId">Approval identifier.</param>
        /// <returns>True when an executing waiter is present.</returns>
        public bool HasWaiter(string approvalId)
        {
            return !String.IsNullOrEmpty(approvalId) && _Waiters.ContainsKey(approvalId);
        }

        /// <summary>
        /// Wait until an approval leaves Pending, or until maxWait passes.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="approvalId">Approval identifier.</param>
        /// <param name="maxWait">Longest wait, or null to wait until the decision or expiry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="executes">True when the caller runs the approved call itself (the original invocation waiting
        /// inline), so the unattended runner must not; false for observers that only want to know the outcome
        /// (hugo_await_approval), in which case the unattended runner executes the call. Default true.</param>
        /// <returns>The status: Pending when maxWait passed first.</returns>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<ApprovalStatusEnum> WaitAsync(string tenantId, string approvalId, TimeSpan? maxWait, CancellationToken token, bool executes = true)
        {
            TaskCompletionSource<ApprovalStatusEnum> signal = new TaskCompletionSource<ApprovalStatusEnum>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (executes) _Waiters[approvalId] = signal;
            else _Observers.GetOrAdd(approvalId, _ => new ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>())[signal] = 0;
            DateTime deadline = maxWait.HasValue ? DateTime.UtcNow + maxWait.Value : DateTime.MaxValue;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    Approval? current = await _Database.Approvals.ReadAsync(tenantId, approvalId, token).ConfigureAwait(false);
                    if (current == null) return ApprovalStatusEnum.Cancelled;
                    if (current.Status != ApprovalStatusEnum.Pending) return current.Status;
                    if (current.ExpiresUtc <= DateTime.UtcNow)
                    {
                        await ExpireAsync(current, token).ConfigureAwait(false);
                        continue;
                    }
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) return ApprovalStatusEnum.Pending;
                    TimeSpan poll = TimeSpan.FromMilliseconds(_PollIntervalMs);
                    if (remaining < poll) poll = remaining;
                    Task finished = await Task.WhenAny(signal.Task, Task.Delay(poll, token)).ConfigureAwait(false);
                    if (finished == signal.Task) return await signal.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                if (executes) _Waiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<ApprovalStatusEnum>>(approvalId, signal));
                else if (_Observers.TryGetValue(approvalId, out ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>? observers))
                {
                    observers.TryRemove(signal, out byte _);
                    if (observers.IsEmpty) _Observers.TryRemove(new KeyValuePair<string, ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>>(approvalId, observers));
                }
            }
        }

        /// <summary>
        /// Approve, reject, or cancel a pending approval.
        /// </summary>
        /// <param name="ctx">Who decides.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Approval identifier.</param>
        /// <param name="status">Approved, Rejected, or Cancelled.</param>
        /// <param name="note">Note, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The approval after the decision (with the invocation result when it ran unattended).</returns>
        /// <exception cref="NotFoundException">Thrown when the approval does not exist.</exception>
        /// <exception cref="ConflictException">Thrown when it is no longer pending (NotPending), or self-approval is not allowed (SelfApprovalNotAllowed).</exception>
        public async Task<Approval> DecideAsync(RequestContext ctx, string tenantId, string id, ApprovalStatusEnum status, string? note, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (status != ApprovalStatusEnum.Approved && status != ApprovalStatusEnum.Rejected && status != ApprovalStatusEnum.Cancelled) throw new ArgumentException("A decision is Approved, Rejected, or Cancelled.", nameof(status));
            if (note != null && note.Length > 2048) throw new ValidationException("The note is longer than 2048 characters.", new Dictionary<string, string> { { "note", "At most 2048 characters." } });
            Approval approval = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (approval.Status != ApprovalStatusEnum.Pending) throw new ConflictException("NotPending", "Approval " + id + " is already " + approval.Status + ".");
            bool self = !String.IsNullOrEmpty(approval.RequestedByPrincipalId) && String.Equals(approval.RequestedByPrincipalId, ctx.PrincipalId, StringComparison.Ordinal);
            if (self && status != ApprovalStatusEnum.Cancelled)
            {
                if (approval.Surface == InvocationSurfaceEnum.A2A) throw new ConflictException("SelfApprovalNotAllowed", "An A2A caller cannot decide its own approval; a human decides it in the dashboard or over REST.");
                if (!_AllowSelfApproval()) throw new ConflictException("SelfApprovalNotAllowed", "This tenant requires someone other than the requester to decide approvals (Approvals.AllowSelfApproval is false).");
            }
            if (approval.ExpiresUtc <= DateTime.UtcNow)
            {
                await ExpireAsync(approval, token).ConfigureAwait(false);
                throw new ConflictException("NotPending", "Approval " + id + " expired.");
            }
            bool won = await _Database.Approvals.TryDecideAsync(tenantId, id, status, ctx.PrincipalId, ctx.PrincipalName, note, token).ConfigureAwait(false);
            if (!won)
            {
                Approval latest = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
                throw new ConflictException("NotPending", "Approval " + id + " is already " + latest.Status + ".");
            }
            Approval decided = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ApprovalDecided, "Approval", id, status + " " + decided.ToolName + " on " + (decided.BindingAlias ?? "?") + ": " + decided.Summary + (String.IsNullOrEmpty(note) ? String.Empty : " Note: " + note), tenantId, token).ConfigureAwait(false);
            await AfterDecisionAsync(decided, token).ConfigureAwait(false);
            return await ReadAsync(tenantId, id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Expire every pending approval past its expiry time (the expiry job, P3.16).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approvals expired.</returns>
        public async Task<int> ExpireDueAsync(CancellationToken token)
        {
            int expired = 0;
            foreach (Approval approval in await _Database.Approvals.ListExpiredAsync(DateTime.UtcNow, 500, token).ConfigureAwait(false))
            {
                if (await ExpireAsync(approval, token).ConfigureAwait(false)) expired++;
            }
            return expired;
        }

        /// <summary>
        /// Cancel every pending approval of a tenant (tenant lockdown, plan section 11.6).
        /// </summary>
        /// <param name="ctx">Who triggered it, or null.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="reason">Reason recorded as the decision note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Approvals cancelled.</returns>
        public async Task<int> CancelPendingAsync(RequestContext? ctx, string tenantId, string reason, CancellationToken token)
        {
            int cancelled = 0;
            foreach (Approval approval in await _Database.Approvals.ListPendingAsync(tenantId, token).ConfigureAwait(false))
            {
                if (!await _Database.Approvals.TryDecideAsync(tenantId, approval.Id, ApprovalStatusEnum.Cancelled, ctx?.PrincipalId, ctx?.PrincipalName ?? "system", reason, token).ConfigureAwait(false)) continue;
                cancelled++;
                Approval decided = await ReadAsync(tenantId, approval.Id, token).ConfigureAwait(false);
                await AfterDecisionAsync(decided, token).ConfigureAwait(false);
            }
            return cancelled;
        }

        /// <summary>
        /// Run the expiry job until cancelled.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_SweepIntervalSeconds), token).ConfigureAwait(false);
                    await HugoTracing.BackgroundAsync("approvals", "expire", async ct => await ExpireDueAsync(ct).ConfigureAwait(false) > 0, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[Approvals] expiry sweep failed: " + SecretScrubber.Shared.Scrub(e.Message));
                }
            }
        }

        /// <summary>
        /// The approval card published in events (no arguments beyond the redacted copy).
        /// </summary>
        /// <param name="approval">Approval.</param>
        /// <returns>Card object.</returns>
        public static object Card(Approval approval)
        {
            return new
            {
                id = approval.Id,
                status = approval.Status.ToString(),
                toolName = approval.ToolName,
                resource = approval.BindingAlias,
                agentProfileId = approval.AgentProfileId,
                effect = approval.Effect.ToString(),
                summary = approval.Summary,
                reason = approval.EffectReason,
                requestedBy = approval.RequestedByName,
                surface = approval.Surface.ToString(),
                argumentsRedacted = approval.ArgumentsRedacted,
                expiresUtc = approval.ExpiresUtc,
                toolInvocationId = approval.ToolInvocationId
            };
        }

        #endregion

        #region Private-Methods

        private async Task<bool> ExpireAsync(Approval approval, CancellationToken token)
        {
            if (!await _Database.Approvals.TryDecideAsync(approval.TenantId, approval.Id, ApprovalStatusEnum.Expired, null, "system", "Expired without a decision.", token).ConfigureAwait(false)) return false;
            Approval decided = await ReadAsync(approval.TenantId, approval.Id, token).ConfigureAwait(false);
            await AfterDecisionAsync(decided, token).ConfigureAwait(false);
            return true;
        }

        private async Task AfterDecisionAsync(Approval decided, CancellationToken token)
        {
            HugoMetrics.Count(HugoMetricCatalog.Approvals, 1, HugoMetrics.Tags("outcome", decided.Status.ToString().ToLowerInvariant()));
            HugoMetrics.Observe(HugoMetricCatalog.ApprovalWait, Math.Max(0, ((decided.DecidedUtc ?? DateTime.UtcNow) - decided.CreatedUtc).TotalSeconds), "effect", decided.Effect.ToString());
            _Events?.Publish("approval.resolved", decided.TenantId, Card(decided));
            if (_Observers.TryGetValue(decided.Id, out ConcurrentDictionary<TaskCompletionSource<ApprovalStatusEnum>, byte>? observers))
            {
                foreach (TaskCompletionSource<ApprovalStatusEnum> observer in observers.Keys) observer.TrySetResult(decided.Status);
            }
            // Exactly one party runs an approved call: the original invocation when it is still waiting inline,
            // otherwise the unattended runner. Observers never run it.
            if (_Waiters.TryGetValue(decided.Id, out TaskCompletionSource<ApprovalStatusEnum>? waiter))
            {
                waiter.TrySetResult(decided.Status);
                return;
            }
            Func<Approval, CancellationToken, Task>? unattended = Unattended;
            if (unattended == null) return;
            try
            {
                await unattended(decided, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Logging.Warn("[Approvals] resuming the call of approval " + decided.Id + " failed: " + SecretScrubber.Shared.Scrub(e.Message));
            }
        }

        #endregion
    }
}
