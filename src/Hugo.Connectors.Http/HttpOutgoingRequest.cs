namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A validated request ready for <see cref="HttpSender"/>. Not thread-safe for mutation.
    /// </summary>
    public class HttpOutgoingRequest
    {
        /// <summary>
        /// Upper-case method. Default GET.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Encoded path relative to the base URL path (from <see cref="HttpPathCheck.EncodedPath"/>). Default <c>/</c>.
        /// </summary>
        public string EncodedPath { get; set; } = "/";

        /// <summary>
        /// Query parameters. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Query { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Extra request headers from the call. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Body bytes, or null for no body.
        /// </summary>
        public byte[]? Body { get; set; } = null;

        /// <summary>
        /// Content type of the body, or null.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Whether to follow redirects (within policy).
        /// </summary>
        public bool FollowRedirects { get; set; } = true;
    }
}
