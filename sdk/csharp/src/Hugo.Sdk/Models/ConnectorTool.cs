namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A tool a connector exposes (preview).
    /// </summary>
    public class ConnectorTool
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// JSON schema of the tool input, as a string.
        /// </summary>
        public string? InputSchemaJson { get; set; } = null;

        /// <summary>
        /// Read, Write, or Destructive.
        /// </summary>
        public string? BaseEffect { get; set; } = null;

        /// <summary>
        /// Classified per call.
        /// </summary>
        public bool ClassifiedPerCall { get; set; } = false;

        /// <summary>
        /// Updates knowledge.
        /// </summary>
        public bool UpdatesKnowledge { get; set; } = false;

        /// <summary>
        /// Requires model.
        /// </summary>
        public bool RequiresModel { get; set; } = false;

        /// <summary>
        /// Output description.
        /// </summary>
        public string? OutputDescription { get; set; } = null;

        #endregion
    }
}
