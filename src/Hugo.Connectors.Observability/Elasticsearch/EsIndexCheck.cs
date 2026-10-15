namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// Outcome of checking an index expression against a resource's allow list. Immutable and thread-safe.
    /// </summary>
    public class EsIndexCheck
    {
        #region Public-Members

        /// <summary>
        /// Whether the expression may be used.
        /// </summary>
        public bool Allowed { get; }

        /// <summary>
        /// Error code when refused: <c>invalid_argument</c> for malformed expressions, <c>permission_denied</c> for
        /// expressions outside the allow list. Empty when allowed.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Reason when refused. Empty when allowed.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Normalized expression (parts trimmed and joined with commas). Empty when refused.
        /// </summary>
        public string Normalized { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="allowed">Whether allowed.</param>
        /// <param name="code">Error code.</param>
        /// <param name="reason">Reason.</param>
        /// <param name="normalized">Normalized expression.</param>
        public EsIndexCheck(bool allowed, string code, string reason, string normalized)
        {
            Allowed = allowed;
            Code = code ?? String.Empty;
            Reason = reason ?? String.Empty;
            Normalized = normalized ?? String.Empty;
        }

        #endregion
    }
}
