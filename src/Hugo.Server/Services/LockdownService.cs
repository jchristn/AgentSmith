namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Tenant lockdown (P3.23, plan section 11.6): while on, the pipeline denies every non-Read call in the tenant;
    /// turning it on cancels pending approvals. Every change is audited (LockdownChanged) and published as
    /// tenant.lockdown. Thread-safe.
    /// </summary>
    public class LockdownService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ApprovalService _Approvals;
        private readonly AuditService _Audit;
        private readonly AuthorizationService _Authorization;
        private readonly EventHub? _Events;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="approvals">Approvals.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="authorization">Authorization (its caches are invalidated so the tenant record is re-read).</param>
        /// <param name="events">Event hub, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LockdownService(DatabaseDriverBase database, ApprovalService approvals, AuditService audit, AuthorizationService authorization, EventHub? events)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _Events = events;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Current lockdown state.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        public async Task<LockdownState> GetAsync(string tenantId, CancellationToken token = default)
        {
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            LockdownState state = new LockdownState();
            state.TenantId = tenant.Id;
            state.Lockdown = tenant.Lockdown;
            return state;
        }

        /// <summary>
        /// Turn lockdown on or off.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="on">True to lock down.</param>
        /// <param name="reason">Reason, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State after the change.</returns>
        public async Task<LockdownState> SetAsync(RequestContext ctx, string tenantId, bool on, string? reason, CancellationToken token = default)
        {
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            LockdownState state = new LockdownState();
            state.TenantId = tenant.Id;
            bool changed = tenant.Lockdown != on;
            if (changed)
            {
                tenant.Lockdown = on;
                await _Database.Tenants.UpdateAsync(tenant, token).ConfigureAwait(false);
                _Authorization.Invalidate();
            }
            if (on) state.ApprovalsCancelled = await _Approvals.CancelPendingAsync(ctx, tenantId, "Cancelled by tenant lockdown.", token).ConfigureAwait(false);
            state.Lockdown = on;
            string description = (on ? "Tenant lockdown turned on" : "Tenant lockdown turned off") + (String.IsNullOrWhiteSpace(reason) ? "." : ": " + reason.Trim()) + (on ? " " + state.ApprovalsCancelled + " pending approvals cancelled." : String.Empty);
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.LockdownChanged, "Tenant", tenantId, description, tenantId, token).ConfigureAwait(false);
            _Events?.Publish("tenant.lockdown", tenantId, new { tenantId = tenantId, lockdown = on, approvalsCancelled = state.ApprovalsCancelled });
            return state;
        }

        #endregion
    }
}
