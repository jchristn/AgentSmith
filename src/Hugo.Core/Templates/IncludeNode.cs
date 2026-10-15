namespace Hugo.Core.Templates
{
    using System;

    /// <summary>
    /// Include of another template by name ({{> name}}).
    /// </summary>
    public class IncludeNode : TemplateNode
    {
        /// <summary>
        /// Template name.
        /// </summary>
        public string Name { get; set; } = String.Empty;
    }
}
