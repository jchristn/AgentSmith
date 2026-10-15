namespace Hugo.Core.Instructions
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// One entry of a compiled instruction source map: which template key, layer, and version produced a range of the
    /// text (Pneuma ResolvedPrompt pattern). Segments nest: an include inside another include has a greater depth. Not
    /// thread-safe.
    /// </summary>
    public class InstructionSegment
    {
        #region Public-Members

        /// <summary>
        /// Template key (for example persona.agent-smith, connector.ssh.instructions), or a pseudo key such as
        /// binding.instructions for binding override text.
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                _Key = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Layer that supplied the template.
        /// </summary>
        public PromptLayerEnum Layer { get; set; } = PromptLayerEnum.BuiltIn;

        /// <summary>
        /// Version of the layer's row (0 for Built-in).
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Binding alias, for per-resource segments.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Start offset in the compiled text.
        /// </summary>
        public int Start { get; set; } = 0;

        /// <summary>
        /// Length in characters.
        /// </summary>
        public int Length { get; set; } = 0;

        /// <summary>
        /// Nesting depth (0 for the root).
        /// </summary>
        public int Depth { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Key = String.Empty;

        #endregion
    }
}
