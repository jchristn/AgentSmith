namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Render preview result.
    /// </summary>
    public class PromptRenderResponse
    {
        /// <summary>
        /// Rendered text, empty when rendering failed.
        /// </summary>
        public string Rendered { get; set; } = String.Empty;

        /// <summary>
        /// Whether rendering succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Variables the template references.
        /// </summary>
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// Templates the template includes.
        /// </summary>
        public List<string> Includes { get; set; } = new List<string>();

        /// <summary>
        /// Validation or render errors with positions.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}
