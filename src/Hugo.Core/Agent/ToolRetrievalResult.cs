namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Tools selected for a turn.
    /// </summary>
    public class ToolRetrievalResult
    {
        #region Public-Members

        /// <summary>
        /// Selected tools: meta tools first, then the best matches. Never null.
        /// </summary>
        public List<AgentToolDefinition> Tools
        {
            get
            {
                return _Tools;
            }
            set
            {
                _Tools = value ?? new List<AgentToolDefinition>();
            }
        }

        /// <summary>
        /// Whether embedding similarity chose the tools (false means every tool was offered).
        /// </summary>
        public bool UsedRetrieval { get; set; } = false;

        /// <summary>
        /// Why retrieval was skipped or failed, when it was.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Similarity score per selected non-meta tool name. Never null.
        /// </summary>
        public Dictionary<string, double> Scores
        {
            get
            {
                return _Scores;
            }
            set
            {
                _Scores = value ?? new Dictionary<string, double>(StringComparer.Ordinal);
            }
        }

        #endregion

        #region Private-Members

        private List<AgentToolDefinition> _Tools = new List<AgentToolDefinition>();
        private Dictionary<string, double> _Scores = new Dictionary<string, double>(StringComparer.Ordinal);

        #endregion
    }
}
