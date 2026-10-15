namespace Hugo.Sdk.Models
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// State of one layer of a prompt template key.
    /// </summary>
    public class PromptLayerState
    {
        #region Public-Members

        /// <summary>
        /// Layer.
        /// </summary>
        public PromptLayerEnum Layer { get; set; } = PromptLayerEnum.Tenant;

        /// <summary>
        /// Exists.
        /// </summary>
        public bool Exists { get; set; } = false;

        /// <summary>
        /// Template Identifier.
        /// </summary>
        public string? TemplateId { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Customized.
        /// </summary>
        public bool Customized { get; set; } = false;

        /// <summary>
        /// Whether the layer below changed after this layer was customized.
        /// </summary>
        public bool DefaultUpdatedSinceCustomized { get; set; } = false;

        /// <summary>
        /// Last update UTC.
        /// </summary>
        public DateTime? LastUpdateUtc { get; set; } = null;

        #endregion
    }
}
