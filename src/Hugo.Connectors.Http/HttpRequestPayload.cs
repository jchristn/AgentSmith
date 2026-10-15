namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of <c>http_request</c>. The body itself is in the text result.
    /// </summary>
    public class HttpRequestPayload
    {
        /// <summary>
        /// Method of the final request.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Final URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Final status code.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// Final reason phrase.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Response headers; Set-Cookie values are redacted. Never null.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Content type, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Body bytes read.
        /// </summary>
        public long BodyBytes { get; set; } = 0;

        /// <summary>
        /// Whether the body was cut at maxResponseBytes.
        /// </summary>
        public bool BodyTruncated { get; set; } = false;

        /// <summary>
        /// Whether the body is text (shown in the text result) or binary (summarized).
        /// </summary>
        public bool BodyIsText { get; set; } = true;

        /// <summary>
        /// Redirects seen. Never null.
        /// </summary>
        public List<HttpRedirectHop> Redirects { get; set; } = new List<HttpRedirectHop>();

        /// <summary>
        /// Whether an OAuth2 token was refreshed and the request retried after a 401.
        /// </summary>
        public bool AuthRetried { get; set; } = false;

        /// <summary>
        /// Seconds from Retry-After, or null.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Total latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;
    }
}
