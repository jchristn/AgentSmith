namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A captured HTTP request with timing and (optionally) bodies and headers.
    /// </summary>
    public class RequestHistoryEntry
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
        /// User Identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Principal name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Path without query.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Path with query.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Source IP address.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Trace Identifier.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Correlation Identifier.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Request body.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Request body bytes.
        /// </summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>
        /// Request body truncated.
        /// </summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>
        /// Response body.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// Response body bytes.
        /// </summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>
        /// Response body truncated.
        /// </summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>
        /// Request start, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Request completion, UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Captured headers (read by identifier only).
        /// </summary>
        public List<RequestHistoryHeader> Headers { get; set; } = new List<RequestHistoryHeader>();

        #endregion
    }
}
