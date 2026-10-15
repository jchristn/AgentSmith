namespace Hugo.Core.Templates
{
    using System;

    /// <summary>
    /// Literal text.
    /// </summary>
    public class TextNode : TemplateNode
    {
        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; set; } = String.Empty;
    }
}
