namespace Hugo.Core.Requests
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// REST dry run or direct invoke of one tool (plan section 14.2): as a profile (by identifier or name, else resolved per
    /// D-018) on a binding alias or a resource identifier.
    /// </summary>
    public class ToolCallRequest
    {
        #region Public-Members

        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Profile name (used when ProfileId is empty).
        /// </summary>
        public string? Profile { get; set; } = null;

        /// <summary>
        /// Binding alias (the resource argument). May also be given inside Arguments.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Resource identifier; the binding of the profile to that resource is used.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name (for example sql_list_tables).
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
        /// Tool arguments as a JSON object.
        /// </summary>
        public JsonElement? Arguments { get; set; } = null;

        #endregion

        #region Private-Members

        private string _ToolName = String.Empty;

        #endregion
    }
}
