namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Save content on a template layer.
    /// </summary>
    public class PromptLayerRequest
    {
        #region Public-Members

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Note stored with the new version.
        /// </summary>
        public string? ChangeNote { get; set; } = null;

        #endregion
    }
}
