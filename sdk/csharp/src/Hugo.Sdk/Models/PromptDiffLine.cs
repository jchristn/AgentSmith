namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// One line of a template diff.
    /// </summary>
    public class PromptDiffLine
    {
        #region Public-Members

        /// <summary>
        /// add, remove, or same.
        /// </summary>
        public string Op { get; set; } = String.Empty;

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        #endregion
    }
}
