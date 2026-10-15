namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Conditional block ({{#if path}} or {{#unless path}}).
    /// </summary>
    public class IfNode : TemplateNode
    {
        /// <summary>
        /// Dotted variable path evaluated for truthiness.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Whether the condition is negated (unless).
        /// </summary>
        public bool Negate { get; set; } = false;

        /// <summary>
        /// Nodes rendered when the condition holds.
        /// </summary>
        public List<TemplateNode> Then { get; set; } = new List<TemplateNode>();

        /// <summary>
        /// Nodes rendered otherwise.
        /// </summary>
        public List<TemplateNode> Else { get; set; } = new List<TemplateNode>();
    }
}
