namespace Hugo.Core.Templates
{
    /// <summary>
    /// A parsed template node.
    /// </summary>
    public abstract class TemplateNode
    {
        /// <summary>
        /// 1-based line where the node starts.
        /// </summary>
        public int Line { get; set; } = 0;

        /// <summary>
        /// 1-based column where the node starts.
        /// </summary>
        public int Column { get; set; } = 0;
    }
}
