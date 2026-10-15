namespace Hugo.Connectors.Http
{
    using System;

    /// <summary>
    /// One redirect response seen while sending a request.
    /// </summary>
    public class HttpRedirectHop
    {
        /// <summary>
        /// Status code of the redirect response (301, 302, 303, 307, 308).
        /// </summary>
        public int Status { get; set; } = 0;

        /// <summary>
        /// URL that answered with the redirect.
        /// </summary>
        public string From { get; set; } = String.Empty;

        /// <summary>
        /// Resolved Location target.
        /// </summary>
        public string Location { get; set; } = String.Empty;

        /// <summary>
        /// Whether the redirect was followed.
        /// </summary>
        public bool Followed { get; set; } = false;

        /// <summary>
        /// Why the redirect was not followed, or a note such as dropped authentication. Null when nothing to say.
        /// </summary>
        public string? Note { get; set; } = null;
    }
}
