namespace Hugo.Connectors.Http
{
    using System;

    /// <summary>
    /// Outcome of <see cref="HttpPathGuard.Check"/>. Immutable and thread-safe.
    /// </summary>
    public class HttpPathCheck
    {
        #region Public-Members

        /// <summary>
        /// Whether the path may be requested.
        /// </summary>
        public bool Allowed { get; }

        /// <summary>
        /// Decoded, normalized path relative to the base URL path (starts with <c>/</c>). Empty when not allowed.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Path re-encoded for the request line (each segment percent-encoded). Empty when not allowed.
        /// </summary>
        public string EncodedPath { get; }

        /// <summary>
        /// Stable error code when not allowed: <c>invalid_argument</c> for a malformed or escaping path,
        /// <c>permission_denied</c> for a path outside the allowed prefixes. Empty when allowed.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Reason when not allowed. Empty when allowed.
        /// </summary>
        public string Reason { get; }

        #endregion

        #region Constructors-and-Factories

        private HttpPathCheck(bool allowed, string path, string encodedPath, string code, string reason)
        {
            Allowed = allowed;
            Path = path;
            EncodedPath = encodedPath;
            Code = code;
            Reason = reason;
        }

        /// <summary>
        /// Build an allowed result.
        /// </summary>
        /// <param name="path">Decoded path.</param>
        /// <param name="encodedPath">Encoded path.</param>
        /// <returns>Result.</returns>
        public static HttpPathCheck Ok(string path, string encodedPath)
        {
            return new HttpPathCheck(true, path ?? "/", encodedPath ?? "/", String.Empty, String.Empty);
        }

        /// <summary>
        /// Build a refused result.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>Result.</returns>
        public static HttpPathCheck Refuse(string code, string reason)
        {
            return new HttpPathCheck(false, String.Empty, String.Empty, code ?? String.Empty, reason ?? String.Empty);
        }

        #endregion
    }
}
