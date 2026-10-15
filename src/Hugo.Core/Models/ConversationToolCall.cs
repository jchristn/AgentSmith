namespace Hugo.Core.Models
{
    using System;

    /// <summary>
    /// A tool call an assistant message requested (child table <c>message_tool_calls</c>). Not thread-safe.
    /// </summary>
    public class ConversationToolCall
    {
        #region Public-Members

        /// <summary>
        /// Call identifier assigned by the model or the runtime.
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
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Arguments as the model produced them, after secret scrubbing. Never null; defaults to an empty object.
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

        #endregion

        #region Private-Members

        private string _CallId = String.Empty;
        private string _Name = String.Empty;
        private string _ArgumentsJson = "{}";

        #endregion
    }
}
