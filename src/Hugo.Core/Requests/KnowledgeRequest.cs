namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Write one resource knowledge entry through the REST routes (operator edits).
    /// </summary>
    public class KnowledgeRequest
    {
        #region Public-Members

        /// <summary>
        /// Content.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Source. Default Operator.
        /// </summary>
        public KnowledgeSourceEnum? Source { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Content = String.Empty;

        #endregion
    }
}
