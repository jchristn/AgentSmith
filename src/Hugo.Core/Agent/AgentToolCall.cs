namespace Hugo.Core.Agent
{
    using System;

    /// <summary>
    /// One tool call requested by the model, handed to <see cref="IAgentToolExecutor"/>.
    /// </summary>
    public class AgentToolCall
    {
        #region Public-Members

        /// <summary>
        /// Call ID from the model (or generated when the model gave none). Unique within the turn.
        /// </summary>
        public string CallId
        {
            get
            {
                return _CallId;
            }
            set
            {
                _CallId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName
        {
            get
            {
                return _ToolName;
            }
            set
            {
                _ToolName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Arguments as a JSON object string, exactly as the model produced them (may be invalid JSON; the
        /// pipeline validates). Default <c>{}</c>.
        /// </summary>
        public string ArgumentsJson
        {
            get
            {
                return _ArgumentsJson;
            }
            set
            {
                _ArgumentsJson = String.IsNullOrWhiteSpace(value) ? "{}" : value;
            }
        }

        /// <summary>
        /// The <c>resource</c> argument (binding alias), when present and a string.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// The tool's definition, when the tool is in the turn's catalog. Null for an unknown tool.
        /// </summary>
        public AgentToolDefinition? Definition { get; set; } = null;

        /// <summary>
        /// Iteration (1-based) in which the model requested the call.
        /// </summary>
        public int Iteration { get; set; } = 1;

        #endregion

        #region Private-Members

        private string _CallId = String.Empty;
        private string _ToolName = String.Empty;
        private string _ArgumentsJson = "{}";

        #endregion
    }
}
