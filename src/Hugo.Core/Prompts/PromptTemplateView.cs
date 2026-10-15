namespace Hugo.Core.Prompts
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Every layer of one template key for a tenant (plan section 10.2). Not thread-safe.
    /// </summary>
    public class PromptTemplateView
    {
        #region Public-Members

        /// <summary>
        /// Template key.
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
        /// Description.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                _Description = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Source of the Built-in (hugo or a connector type), or custom.
        /// </summary>
        public string Source
        {
            get
            {
                return _Source;
            }
            set
            {
                _Source = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Layer the key resolves to.
        /// </summary>
        public PromptLayerEnum ActiveLayer { get; set; } = PromptLayerEnum.BuiltIn;

        /// <summary>
        /// Content the key resolves to.
        /// </summary>
        public string EffectiveContent
        {
            get
            {
                return _EffectiveContent;
            }
            set
            {
                _EffectiveContent = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Built-in content, or null for a key with no Built-in.
        /// </summary>
        public string? BuiltInContent { get; set; } = null;

        /// <summary>
        /// Built-in content hash.
        /// </summary>
        public string? BuiltInHash { get; set; } = null;

        /// <summary>
        /// Global layer.
        /// </summary>
        public PromptLayerState? Global { get; set; } = null;

        /// <summary>
        /// Tenant layer.
        /// </summary>
        public PromptLayerState? Tenant { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Key = String.Empty;
        private string _Description = String.Empty;
        private string _Source = String.Empty;
        private string _EffectiveContent = String.Empty;

        #endregion
    }
}
