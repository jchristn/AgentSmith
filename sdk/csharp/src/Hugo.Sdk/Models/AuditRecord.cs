namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// An audit record. Event types and results are strings so new server values never break deserialization.
    /// </summary>
    public class AuditRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Event type, for example SessionIssued, SecretRevealed, or AuthorizationDenied.
        /// </summary>
        public string EventType { get; set; } = String.Empty;

        /// <summary>
        /// Request Identifier.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Correlation Identifier.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Trace Identifier.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Auth scheme.
        /// </summary>
        public string? AuthScheme { get; set; } = null;

        /// <summary>
        /// Principal type.
        /// </summary>
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Session Identifier.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Resource type.
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Operation.
        /// </summary>
        public string? Operation { get; set; } = null;

        /// <summary>
        /// Request type.
        /// </summary>
        public string? RequestType { get; set; } = null;

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Request path.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Source IP address.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Authentication result, for example Success or Invalid.
        /// </summary>
        public string? AuthResult { get; set; } = null;

        /// <summary>
        /// Authorization result, for example Permitted or DeniedImplicit.
        /// </summary>
        public string? AuthorizationResult { get; set; } = null;

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Bypass reason.
        /// </summary>
        public string? BypassReason { get; set; } = null;

        /// <summary>
        /// HTTP status code (0 when not request-bound).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
