namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Create or update a prompt template.
    /// </summary>
    public class PromptTemplateRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public PromptTemplateKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Variables.
        /// </summary>
        public List<PromptTemplateVariable>? Variables { get; set; } = null;

        /// <summary>
        /// Note stored with the new version.
        /// </summary>
        public string? ChangeNote { get; set; } = null;

        #endregion
    }
}
