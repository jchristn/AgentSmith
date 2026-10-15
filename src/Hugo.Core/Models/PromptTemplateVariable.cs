namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Child record of PromptTemplate: declared variables.
    /// </summary>
    public class PromptTemplateVariable
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewPromptTemplateVariableId() : value;
            }
        }

        /// <summary>
        /// Parent PromptTemplate identifier.
        /// </summary>
        public string PromptTemplateId
        {
            get
            {
                return _PromptTemplateId;
            }
            set
            {
                _PromptTemplateId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Dotted variable path.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether the variable must be present.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Default value when absent.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewPromptTemplateVariableId();
        private string _PromptTemplateId = String.Empty;
        private string _Name = String.Empty;

        #endregion
    }
}
