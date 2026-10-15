namespace Hugo.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Templates;

    /// <summary>
    /// Shared validation for configuration records (forked from Garrison ConfigurationValidator; the Garrison-specific
    /// validators were removed). Thread-safe (stateless).
    /// </summary>
    public static class ConfigurationValidator
    {
        #region Public-Methods

        /// <summary>
        /// Validate a record name; tenant names cannot use the built-in prefix.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="errors">Error collection.</param>
        /// <param name="field">Field name.</param>
        public static void Name(string? name, Dictionary<string, string> errors, string field = "name")
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (String.IsNullOrWhiteSpace(name)) errors[field] = "A name is required.";
            else if (name.StartsWith(Constants.BuiltInPrefix, StringComparison.OrdinalIgnoreCase)) errors[field] = "Names starting with " + Constants.BuiltInPrefix + " are reserved for built-ins.";
            else if (name.Length > 128) errors[field] = "Names are limited to 128 characters.";
        }

        /// <summary>
        /// Validate template syntax.
        /// </summary>
        /// <param name="template">Template text; null or empty is valid.</param>
        /// <param name="allowSecrets">Whether secret references are allowed.</param>
        /// <param name="errors">Error collection.</param>
        /// <param name="field">Field name.</param>
        public static void Template(string? template, bool allowSecrets, Dictionary<string, string> errors, string field)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            if (String.IsNullOrEmpty(template)) return;
            TemplateAnalysis analysis = TemplateAnalyzer.Analyze(template, field, allowSecrets);
            if (!analysis.Valid) errors[field] = analysis.Errors[0];
        }

        /// <summary>
        /// Validate a prompt template's name and content.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="content">Content.</param>
        /// <exception cref="ValidationException">Thrown when invalid.</exception>
        public static void PromptTemplate(string? name, string? content)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            Name(name, errors);
            if (content == null) errors["content"] = "Content is required.";
            else Template(content, false, errors, "content");
            if (errors.Count > 0) throw new ValidationException("The prompt template is invalid.", errors);
        }

        #endregion
    }
}
