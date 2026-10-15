namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// A prompt template key with its Built-in, Global, and Tenant layers.
    /// </summary>
    public class PromptTemplateView
    {
        #region Public-Members

        /// <summary>
        /// Template key, for example persona.agent-smith.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Source.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Layer in effect.
        /// </summary>
        public PromptLayerEnum ActiveLayer { get; set; } = PromptLayerEnum.BuiltIn;

        /// <summary>
        /// Content in effect.
        /// </summary>
        public string EffectiveContent { get; set; } = String.Empty;

        /// <summary>
        /// Built in content.
        /// </summary>
        public string? BuiltInContent { get; set; } = null;

        /// <summary>
        /// Built in hash.
        /// </summary>
        public string? BuiltInHash { get; set; } = null;

        /// <summary>
        /// Global.
        /// </summary>
        public PromptLayerState? Global { get; set; } = null;

        /// <summary>
        /// Tenant.
        /// </summary>
        public PromptLayerState? Tenant { get; set; } = null;

        #endregion
    }
}
