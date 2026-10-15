namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Create or update a prompt template. Updating content creates a new version.
    /// </summary>
    public class PromptTemplateRequest
    {
        /// <summary>
        /// Template name, unique per tenant. Names starting with builtin/ are reserved.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Kind.
        /// </summary>
        public PromptTemplateKindEnum Kind { get; set; } = PromptTemplateKindEnum.Other;

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Declared variables.
        /// </summary>
        public List<Hugo.Core.Models.PromptTemplateVariable> Variables { get; set; } = new List<Hugo.Core.Models.PromptTemplateVariable>();

        /// <summary>
        /// Note stored with the new version.
        /// </summary>
        public string? ChangeNote { get; set; } = null;
    }
}
