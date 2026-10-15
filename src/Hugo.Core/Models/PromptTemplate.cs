namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Named, versioned prompt or file template.
    /// </summary>
    public class PromptTemplate
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewPromptTemplateId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier. Null for global or built-in records.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Template name, unique per tenant. Built-ins use the builtin/ prefix.
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
        /// Template kind.
        /// </summary>
        public PromptTemplateKindEnum Kind { get; set; } = PromptTemplateKindEnum.Other;

        /// <summary>
        /// Current content.
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
        /// Current version number.
        /// </summary>
        public int CurrentVersion { get; set; } = 1;

        /// <summary>
        /// Whether the template is seeded by the platform.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Stable built-in key.
        /// </summary>
        public string? BuiltInKey { get; set; } = null;

        /// <summary>
        /// Whether the record is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the record is protected from deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Declared variables.
        /// </summary>
        public List<PromptTemplateVariable> Variables
        {
            get
            {
                return _Variables;
            }
            set
            {
                _Variables = value ?? new List<PromptTemplateVariable>();
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewPromptTemplateId();
        private string _Name = String.Empty;
        private string _Content = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;
        private List<PromptTemplateVariable> _Variables = new List<PromptTemplateVariable>();

        #endregion
    }
}
