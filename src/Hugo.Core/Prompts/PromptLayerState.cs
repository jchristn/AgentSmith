namespace Hugo.Core.Prompts
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// One editable layer (Global or Tenant) of a template key, as shown by the layered template routes. Not thread-safe.
    /// </summary>
    public class PromptLayerState
    {
        #region Public-Members

        /// <summary>
        /// Layer.
        /// </summary>
        public PromptLayerEnum Layer { get; set; } = PromptLayerEnum.Global;

        /// <summary>
        /// Whether the layer has an active row.
        /// </summary>
        public bool Exists { get; set; } = false;

        /// <summary>
        /// Row identifier.
        /// </summary>
        public string? TemplateId { get; set; } = null;

        /// <summary>
        /// Content, when the layer has a row.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Current version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Whether the content differs from the layer below it at the time it was last synchronized.
        /// </summary>
        public bool Customized { get; set; } = false;

        /// <summary>
        /// Whether the layer below changed after this layer was customized (for the Global layer: Built-in
        /// updated since you customized this).
        /// </summary>
        public bool DefaultUpdatedSinceCustomized { get; set; } = false;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime? LastUpdateUtc { get; set; } = null;

        #endregion
    }
}
