namespace Hugo.Core.Templates
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of static template analysis.
    /// </summary>
    public class TemplateAnalysis
    {
        /// <summary>
        /// Variable paths referenced (outside loop-relative paths).
        /// </summary>
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// Included template names.
        /// </summary>
        public List<string> Includes { get; set; } = new List<string>();

        /// <summary>
        /// Secret references.
        /// </summary>
        public List<string> SecretReferences { get; set; } = new List<string>();

        /// <summary>
        /// Problems found (empty when the template is valid).
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Whether the template is valid.
        /// </summary>
        public bool Valid
        {
            get
            {
                return Errors.Count == 0;
            }
        }
    }
}
