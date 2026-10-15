namespace Hugo.Server.Routes
{
    using System;
    using WatsonWebserver.Core;

    /// <summary>
    /// Builds the URLs Hugo shows to people and external clients (A2A agent cards, server info). These use
    /// Rest.ExternalBaseUrl when it is set, and otherwise the scheme and host of the request being answered. They never
    /// use Rest.PublicBaseUrl, which is the internal URL handed to runs. Stateless and thread-safe.
    /// </summary>
    public static class ExternalUrls
    {
        #region Public-Methods

        /// <summary>
        /// External REST base URL without a trailing slash.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="http">HTTP context of the request being answered, or null when there is none.</param>
        /// <returns>URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public static string RestBaseUrl(ServerContext context, HttpContextBase? http)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!String.IsNullOrWhiteSpace(context.Settings.Rest.ExternalBaseUrl)) return context.Settings.Rest.ExternalBaseUrl!.Trim().TrimEnd('/');

            string? host = http?.Request.Headers["Host"];
            if (!String.IsNullOrWhiteSpace(host)) return Scheme(context, http!) + "://" + host.Trim();
            return BindBaseUrl(context);
        }

        /// <summary>
        /// REST base URL derived from the bind address, for example http://127.0.0.1:8800. Used when nothing better is known.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <returns>URL.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public static string BindBaseUrl(ServerContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            string hostname = context.Settings.Rest.Hostname;
            string host = hostname == "*" || hostname == "0.0.0.0" || hostname == "+" ? "127.0.0.1" : hostname;
            return (context.Settings.Rest.Ssl ? "https://" : "http://") + host + ":" + context.Settings.Rest.Port;
        }

        #endregion

        #region Private-Methods

        private static string Scheme(ServerContext context, HttpContextBase http)
        {
            string? forwarded = http.Request.Headers["X-Forwarded-Proto"];
            if (!String.IsNullOrWhiteSpace(forwarded))
            {
                string first = forwarded.Split(',')[0].Trim().ToLowerInvariant();
                if (first == "http" || first == "https") return first;
            }
            return context.Settings.Rest.Ssl ? "https" : "http";
        }

        #endregion
    }
}
