namespace Hugo.Core.Prompts
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// The content a template key resolves to for a tenant (most specific of Tenant, Global, Built-in), with where it came
    /// from. Not thread-safe.
    /// </summary>
    public class ResolvedPromptTemplate
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
        /// Content.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Layer that supplied the content.
        /// </summary>
        public PromptLayerEnum Layer { get; set; } = PromptLayerEnum.BuiltIn;

        /// <summary>
        /// Version of that layer's row (0 for Built-in).
        /// </summary>
        public int Version { get; set; } = 0;

        /// <summary>
        /// Row identifier (null for Built-in).
        /// </summary>
        public string? TemplateId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Key = String.Empty;
        private string _Content = String.Empty;

        #endregion
    }
}
