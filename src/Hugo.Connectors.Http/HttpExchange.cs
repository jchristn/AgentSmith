namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The final response of a request after redirects, with the body read up to the byte cap. Not thread-safe for
    /// mutation.
    /// </summary>
    public class HttpExchange
    {
        #region Public-Members

        /// <summary>
        /// Method of the final request (a 303 turns POST into GET).
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Final URL requested.
        /// </summary>
        public string FinalUrl { get; set; } = String.Empty;

        /// <summary>
        /// Status code of the final response.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Reason phrase of the final response.
        /// </summary>
        public string ReasonPhrase { get; set; } = String.Empty;

        /// <summary>
        /// Response and content headers in order. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Content type of the body, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Content-Length announced by the server, or null.
        /// </summary>
        public long? ContentLength { get; set; } = null;

        /// <summary>
        /// Body bytes read (at most the resource's maxResponseBytes). Never null.
        /// </summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Whether the body was longer than maxResponseBytes and was cut.
        /// </summary>
        public bool BodyTruncated { get; set; } = false;

        /// <summary>
        /// Redirects seen, in order. Never null.
        /// </summary>
        public List<HttpRedirectHop> Redirects { get; set; } = new List<HttpRedirectHop>();

        /// <summary>
        /// Whether an OAuth2 token was refreshed after a 401 and the request retried.
        /// </summary>
        public bool AuthRetried { get; set; } = false;

        /// <summary>
        /// Seconds from a Retry-After header, or null.
        /// </summary>
        public double? RetryAfterSeconds { get; set; } = null;

        /// <summary>
        /// Total time across every hop, in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// First value of a header (case-insensitive), or null.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Value or null.</returns>
        public string? Header(string name)
        {
            KeyValuePair<string, string> found = Headers.FirstOrDefault(h => String.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));
            return found.Key == null ? null : found.Value;
        }

        #endregion
    }
}
