namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Loop over a list ({{#each path}}). Inside, this refers to the element and @index, @first, @last describe the position.
    /// </summary>
    public class EachNode : TemplateNode
    {
        /// <summary>
        /// Dotted variable path of the list.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Loop body.
        /// </summary>
        public List<TemplateNode> Body { get; set; } = new List<TemplateNode>();
    }
}
