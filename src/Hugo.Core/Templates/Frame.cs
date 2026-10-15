namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Parser frame for an open block.
    /// </summary>
    public class Frame
    {
        /// <summary>
        /// Block kind: if, unless, each, or empty for the root.
        /// </summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Node list receiving parsed children.
        /// </summary>
        public List<TemplateNode> Target { get; set; } = new List<TemplateNode>();

        /// <summary>
        /// Conditional node, for if and unless frames.
        /// </summary>
        public IfNode? If { get; set; } = null;

        /// <summary>
        /// Whether the else branch is active.
        /// </summary>
        public bool InElse { get; set; } = false;

        /// <summary>
        /// Opening token.
        /// </summary>
        public Token? Token { get; set; } = null;
    }
}
