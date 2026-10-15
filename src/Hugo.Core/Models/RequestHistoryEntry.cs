namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Captured HTTP request and response.
    /// </summary>
    public class RequestHistoryEntry
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewRequestHistoryEntryId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier. Null for global or built-in records.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Principal display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method
        {
            get
            {
                return _Method;
            }
            set
            {
                _Method = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Route path.
        /// </summary>
        public string Path
        {
            get
            {
                return _Path;
            }
            set
            {
                _Path = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Full URL including query.
        /// </summary>
        public string Url
        {
            get
            {
                return _Url;
            }
            set
            {
                _Url = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Response status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Source IP.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// W3C trace identifier of the request, or null.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Correlation identifier (x-request-id), or null.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Request body, possibly truncated.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// Original request body length in bytes.
        /// </summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the request body was truncated.
        /// </summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>
        /// Response body, possibly truncated.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// Original response body length in bytes.
        /// </summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the response body was truncated.
        /// </summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>
        /// Request start (UTC).
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
        /// Response completion (UTC).
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Captured request and response headers (secrets redacted).
        /// </summary>
        public List<RequestHistoryHeader> Headers
        {
            get
            {
                return _Headers;
            }
            set
            {
                _Headers = value ?? new List<RequestHistoryHeader>();
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewRequestHistoryEntryId();
        private string _Method = String.Empty;
        private string _Path = String.Empty;
        private string _Url = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private List<RequestHistoryHeader> _Headers = new List<RequestHistoryHeader>();

        #endregion
    }
}
