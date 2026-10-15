namespace Hugo.Sdk.Models
{
    /// <summary>
    /// Source map entry of compiled instructions.
    /// </summary>
    public class InstructionSegment
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Layer.
        /// </summary>
        public string? Layer { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Binding alias.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Start.
        /// </summary>
        public int Start { get; set; } = 0;

        /// <summary>
        /// Length.
        /// </summary>
        public int Length { get; set; } = 0;

        /// <summary>
        /// Depth.
        /// </summary>
        public int Depth { get; set; } = 0;

        #endregion
    }
}
