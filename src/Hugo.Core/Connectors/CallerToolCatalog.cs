namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The tools one caller sees for one profile (GET /v1.0/api/agent-profiles/{id}/tools, MCP tools/list, chat tool
    /// definitions). Not thread-safe.
    /// </summary>
    public class CallerToolCatalog
    {
        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>
        /// Profile name.
        /// </summary>
        public string ProfileName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the profile has an inference endpoint (tools that require a model are listed only then).
        /// </summary>
        public bool HasModel { get; set; } = false;

        /// <summary>
        /// Resources the caller can use.
        /// </summary>
        public int ResourceCount { get; set; } = 0;

        /// <summary>
        /// Connector tools, ordered by name.
        /// </summary>
        public List<CallerTool> Tools { get; set; } = new List<CallerTool>();

        /// <summary>
        /// Hugo meta tools.
        /// </summary>
        public List<CallerTool> MetaTools { get; set; } = new List<CallerTool>();
    }
}
