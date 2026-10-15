namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A variable a prompt template declares.
    /// </summary>
    public class PromptTemplateVariable
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Prompt template Identifier.
        /// </summary>
        public string? PromptTemplateId { get; set; } = null;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Default value.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        #endregion
    }
}
