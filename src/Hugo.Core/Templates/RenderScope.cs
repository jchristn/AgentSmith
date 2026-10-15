namespace Hugo.Core.Templates
{
    /// <summary>
    /// Loop scope while rendering an each block.
    /// </summary>
    public class RenderScope
    {
        /// <summary>
        /// Current element (this).
        /// </summary>
        public object? Current { get; set; } = null;

        /// <summary>
        /// Zero-based index.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Element count.
        /// </summary>
        public int Count { get; set; } = 0;
    }
}
