namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Preview of a profile's A2A agent card.
    /// </summary>
    public class AgentCardPreview
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// URL.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Skills.
        /// </summary>
        public List<AgentCardSkill> Skills { get; set; } = new List<AgentCardSkill>();

        #endregion
    }
}
