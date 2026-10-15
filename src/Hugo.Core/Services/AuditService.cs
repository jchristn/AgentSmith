namespace Hugo.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Writes audit records. Failures are reported to the diagnostic callback and never thrown. Thread-safe.
    /// </summary>
    public class AuditService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly Action<string>? _Diagnostic;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="diagnostic">Optional diagnostic callback for write failures.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public AuditService(DatabaseDriverBase database, Action<string>? diagnostic = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Diagnostic = diagnostic;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write an audit record describing an action by the principal in the context.
        /// </summary>
        /// <param name="context">Request context, or null for system actions.</param>
        /// <param name="eventType">Event type.</param>
        /// <param name="resourceType">Resource type involved.</param>
        /// <param name="resourceId">Resource identifier involved.</param>
        /// <param name="description">Short description.</param>
        /// <param name="tenantId">Tenant override; defaults to the context tenant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WriteAsync(RequestContext? context, AuditEventTypeEnum eventType, string? resourceType, string? resourceId, string? description, string? tenantId = null, CancellationToken token = default)
        {
            AuditRecord record = new AuditRecord();
            record.EventType = eventType;
            record.TenantId = tenantId ?? context?.TenantId;
            record.ResourceType = resourceType;
            record.ResourceId = resourceId;
            record.Description = description;
            if (context != null)
            {
                record.RequestId = context.RequestId;
                record.AuthScheme = context.AuthScheme;
                record.PrincipalType = context.PrincipalType;
                record.PrincipalId = context.PrincipalId;
                record.PrincipalName = context.PrincipalName;
                record.SessionId = context.SessionId;
                record.SourceIp = context.SourceIp;
                record.Operation = context.Operation?.ToString();
                record.AuthResult = context.AuthResult;
                record.AuthorizationResult = context.AuthorizationResult;
            }
            await WriteAsync(record, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Write a prepared audit record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task WriteAsync(AuditRecord record, CancellationToken token = default)
        {
            if (record == null) return;
            record.Description = SecretScrubber.Shared.Scrub(record.Description);
            record.Reason = SecretScrubber.Shared.Scrub(record.Reason);
            if (String.IsNullOrEmpty(record.TraceId)) record.TraceId = System.Diagnostics.Activity.Current?.TraceId.ToHexString();
            HugoMetrics.Count(HugoMetricCatalog.AuditEvents, "type", record.EventType.ToString());
            try
            {
                await _Database.Audit.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Diagnostic?.Invoke("audit write failed: " + e.Message);
            }
        }

        #endregion
    }
}
