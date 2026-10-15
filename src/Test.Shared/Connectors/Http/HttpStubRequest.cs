namespace Test.Shared.Connectors.Http
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request received by <see cref="HttpStubServer"/>.
    /// </summary>
    public class HttpStubRequest
    {
        /// <summary>
        /// Method.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Raw request target path (still percent-encoded), without the query.
        /// </summary>
        public string Path { get; set; } = "/";

        /// <summary>
        /// Raw query string without the leading '?', or empty.
        /// </summary>
        public string Query { get; set; } = String.Empty;

        /// <summary>
        /// Headers (case-insensitive names; repeated headers joined with ", ").
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Body decoded as UTF-8.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Value of a header, or null.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Value or null.</returns>
        public string? Header(string name)
        {
            return Headers.TryGetValue(name, out string? v) ? v : null;
        }

        /// <summary>
        /// Decoded value of a query parameter, or null.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <returns>Value or null.</returns>
        public string? QueryValue(string name)
        {
            foreach (string pair in Query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(eq < 0 ? pair : pair.Substring(0, eq));
                if (key == name) return eq < 0 ? String.Empty : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace("+", "%20", StringComparison.Ordinal));
            }
            return null;
        }
    }
}
