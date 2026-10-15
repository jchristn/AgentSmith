namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Write a resource knowledge entry (preview).
    /// </summary>
    public class KnowledgeRequest
    {
        #region Public-Members

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Operator (default), Generated, Agent, or Derived.
        /// </summary>
        public string? Source { get; set; } = null;

        #endregion
    }
}
