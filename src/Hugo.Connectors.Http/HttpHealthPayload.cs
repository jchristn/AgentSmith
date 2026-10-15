namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Structured result of <c>http_health_check</c>.
    /// </summary>
    public class HttpHealthPayload
    {
        /// <summary>
        /// Whether every check passed.
        /// </summary>
        public bool Healthy { get; set; } = false;

        /// <summary>
        /// Method used (GET or HEAD).
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// URL first requested.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Final URL after redirects.
        /// </summary>
        public string FinalUrl { get; set; } = String.Empty;

        /// <summary>
        /// Final status code, or 0 when no response arrived.
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// Expected statuses, as text (for example <c>2xx or 3xx</c>).
        /// </summary>
        public string Expected { get; set; } = "2xx or 3xx";

        /// <summary>
        /// Whether the status met the expectation.
        /// </summary>
        public bool StatusOk { get; set; } = false;

        /// <summary>
        /// Total latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;

        /// <summary>
        /// Redirects seen. Never null.
        /// </summary>
        public List<HttpRedirectHop> Redirects { get; set; } = new List<HttpRedirectHop>();

        /// <summary>
        /// Certificate of the final https endpoint, or null for plain http or when none was seen.
        /// </summary>
        public HttpCertificateInfo? Tls { get; set; } = null;

        /// <summary>
        /// Text the body had to contain, or null.
        /// </summary>
        public string? BodyContains { get; set; } = null;

        /// <summary>
        /// Whether the body contained <see cref="BodyContains"/>, or null when not checked.
        /// </summary>
        public bool? BodyMatched { get; set; } = null;

        /// <summary>
        /// Error code when no response arrived (for example <c>connection_failed</c>), or null.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message when no response arrived, or null.
        /// </summary>
        public string? Error { get; set; } = null;
    }
}
