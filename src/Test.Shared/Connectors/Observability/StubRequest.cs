namespace Test.Shared.Connectors.Observability
{
    using System;
    using System.Collections.Specialized;

    /// <summary>
    /// A request received by <see cref="ObservabilityStub"/>.
    /// </summary>
    public class StubRequest
    {
        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Absolute path, URL-decoded.
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// Raw path and query as received.
        /// </summary>
        public string RawUrl { get; set; } = "/";

        /// <summary>
        /// Query parameters.
        /// </summary>
        public NameValueCollection Query { get; set; } = new NameValueCollection();

        /// <summary>
        /// Request headers.
        /// </summary>
        public NameValueCollection Headers { get; set; } = new NameValueCollection();

        /// <summary>
        /// Body text.
        /// </summary>
        public string Body { get; set; } = String.Empty;
    }
}
