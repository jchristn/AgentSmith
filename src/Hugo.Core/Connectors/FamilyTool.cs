namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A tool as exposed per family: one descriptor shared by every connector type in the family that offers it.
    /// </summary>
    public class FamilyTool
    {
        /// <summary>
        /// Family.
        /// </summary>
        public string Family { get; set; } = String.Empty;

        /// <summary>
        /// Descriptor (from the first registered connector offering the tool).
        /// </summary>
        public ToolDescriptor Descriptor { get; set; } = new ToolDescriptor();

        /// <summary>
        /// Connector types offering the tool. Never null.
        /// </summary>
        public List<string> ConnectorTypes { get; set; } = new List<string>();
    }
}
