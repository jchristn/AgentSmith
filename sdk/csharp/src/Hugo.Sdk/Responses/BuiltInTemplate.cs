namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// The shipped Built-in content of a template key.
    /// </summary>
    public class BuiltInTemplate
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Hash.
        /// </summary>
        public string? Hash { get; set; } = null;

        /// <summary>
        /// Source.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
