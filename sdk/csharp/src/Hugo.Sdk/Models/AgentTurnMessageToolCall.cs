namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A tool call requested by a message produced during a turn.
    /// </summary>
    public class AgentTurnMessageToolCall
    {
        #region Public-Members

        /// <summary>
        /// Call identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Arguments as JSON text.
        /// </summary>
        public string ArgumentsJson { get; set; } = String.Empty;

        #endregion
    }
}
