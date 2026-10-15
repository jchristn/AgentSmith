namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Render template content with sample data.
    /// </summary>
    public class PromptRenderRequest
    {
        #region Public-Members

        /// <summary>
        /// Content to render (ad hoc renders).
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Sample data object the template references.
        /// </summary>
        public object? Context { get; set; } = null;

        /// <summary>
        /// Deployment Identifier.
        /// </summary>
        public string? DeploymentId { get; set; } = null;

        #endregion
    }
}
