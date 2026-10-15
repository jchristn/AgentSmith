namespace Hugo.Cli
{
    using System;

    /// <summary>
    /// Validates and normalizes Hugo server base URLs. Thread-safe (stateless).
    /// </summary>
    public static class ServerAddress
    {
        /// <summary>
        /// The server used when none is configured: <c>http://127.0.0.1:8800</c>.
        /// </summary>
        public static string Default
        {
            get
            {
                return "http://127.0.0.1:8800";
            }
        }

        /// <summary>
        /// Normalize a server base URL: absolute http or https, lowercase scheme and host, no query or fragment, no
        /// trailing slash. A path prefix (a server behind a reverse proxy) is kept.
        /// </summary>
        /// <param name="value">URL text.</param>
        /// <returns>Normalized URL.</returns>
        /// <exception cref="CliUsageException">Thrown when the value is empty or not an absolute http or https URL.</exception>
        public static string Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new CliUsageException("The server URL is empty.");
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new CliUsageException("The server URL '" + value + "' is not an absolute http or https URL, for example http://127.0.0.1:8800.");
            if (!String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment))
                throw new CliUsageException("The server URL '" + value + "' must not have a query or fragment.");
            string authority = uri.Scheme + "://" + uri.Authority.ToLowerInvariant();
            string path = uri.AbsolutePath.TrimEnd('/');
            return authority + path;
        }

        /// <summary>
        /// The MCP endpoint for a server and optional profile: <c>{server}/mcp</c> or <c>{server}/mcp/{profile}</c>,
        /// with the profile name percent-encoded (spaces become <c>%20</c>).
        /// </summary>
        /// <param name="server">Normalized server URL.</param>
        /// <param name="profile">Profile name, or null.</param>
        /// <returns>Endpoint URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the server is null or empty.</exception>
        public static Uri McpEndpoint(string server, string? profile)
        {
            if (String.IsNullOrEmpty(server)) throw new ArgumentNullException(nameof(server));
            string url = server.TrimEnd('/') + "/mcp";
            if (!String.IsNullOrWhiteSpace(profile)) url += "/" + Uri.EscapeDataString(profile.Trim());
            return new Uri(url, UriKind.Absolute);
        }
    }
}
