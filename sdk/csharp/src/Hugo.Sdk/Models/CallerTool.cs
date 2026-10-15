namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A tool as a caller of a profile sees it (preview).
    /// </summary>
    public class CallerTool
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Family.
        /// </summary>
        public string? Family { get; set; } = null;

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Input schema JSON.
        /// </summary>
        public string? InputSchemaJson { get; set; } = null;

        /// <summary>
        /// Base effect.
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
        /// Known effect.
        /// </summary>
        public string? KnownEffect { get; set; } = null;

        /// <summary>
        /// Is meta tool.
        /// </summary>
        public bool IsMetaTool { get; set; } = false;

        /// <summary>
        /// Resources.
        /// </summary>
        public List<CallerToolResource> Resources { get; set; } = new List<CallerToolResource>();

        #endregion
    }
}
