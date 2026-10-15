namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Tools as a caller of a profile sees them.
    /// </summary>
    public class CallerToolCatalog
    {
        #region Public-Members

        /// <summary>
        /// Profile Identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Profile name.
        /// </summary>
        public string? ProfileName { get; set; } = null;

        /// <summary>
        /// Has model.
        /// </summary>
        public bool HasModel { get; set; } = false;

        /// <summary>
        /// Resource count.
        /// </summary>
        public int ResourceCount { get; set; } = 0;

        /// <summary>
        /// Tools.
        /// </summary>
        public List<CallerTool> Tools { get; set; } = new List<CallerTool>();

        /// <summary>
        /// Meta tools.
        /// </summary>
        public List<CallerTool> MetaTools { get; set; } = new List<CallerTool>();

        #endregion
    }
}
