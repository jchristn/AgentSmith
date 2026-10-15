namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Security and configuration audit record.
    /// </summary>
    public class AuditRecord
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewAuditRecordId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier. Null for global or built-in records.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Event type.
        /// </summary>
        public AuditEventTypeEnum EventType { get; set; } = AuditEventTypeEnum.ConfigurationChanged;

        /// <summary>
        /// Request identifier.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Correlation identifier.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Trace identifier.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Authentication scheme.
        /// </summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum? PrincipalType { get; set; } = null;

        /// <summary>
        /// Principal identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Session identifier.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Resource type involved.
        /// </summary>
        public string? ResourceType { get; set; } = null;

        /// <summary>
        /// Resource identifier involved.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Operation involved.
        /// </summary>
        public string? Operation { get; set; } = null;

        /// <summary>
        /// Classified request type.
        /// </summary>
        public string? RequestType { get; set; } = null;

        /// <summary>
        /// HTTP method or protocol verb.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Request path or tool name.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Source IP address.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Authentication result.
        /// </summary>
        public AuthResultEnum? AuthResult { get; set; } = null;

        /// <summary>
        /// Authorization result.
        /// </summary>
        public AuthorizationResultEnum? AuthorizationResult { get; set; } = null;

        /// <summary>
        /// Denial or change reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Bypass reason when authorization was skipped.
        /// </summary>
        public string? BypassReason { get; set; } = null;

        /// <summary>
        /// Response status code, when applicable.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Short description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewAuditRecordId();
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
