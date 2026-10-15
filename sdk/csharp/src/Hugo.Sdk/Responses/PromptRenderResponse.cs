namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of rendering a template.
    /// </summary>
    public class PromptRenderResponse
    {
        #region Public-Members

        /// <summary>
        /// Rendered text.
        /// </summary>
        public string? Rendered { get; set; } = null;

        /// <summary>
        /// Success.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Variables.
        /// </summary>
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// Includes.
        /// </summary>
        public List<string> Includes { get; set; } = new List<string>();

        /// <summary>
        /// Errors.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        #endregion
    }
}
