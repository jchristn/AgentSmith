namespace Hugo.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A skill on an A2A agent card.
    /// </summary>
    public class AgentCardSkill
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Examples.
        /// </summary>
        public List<string> Examples { get; set; } = new List<string>();

        #endregion
    }
}
