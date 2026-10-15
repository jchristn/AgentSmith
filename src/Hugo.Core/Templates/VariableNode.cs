namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Variable output with optional filters.
    /// </summary>
    public class VariableNode : TemplateNode
    {
        /// <summary>
        /// Dotted variable path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Filters applied left to right.
        /// </summary>
        public List<TemplateFilter> Filters { get; set; } = new List<TemplateFilter>();
    }
}
