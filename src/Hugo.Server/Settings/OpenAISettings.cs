namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// OpenAI-compatible surface settings (used from Phase 7).
    /// </summary>
    public class OpenAISettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the OpenAI-compatible surface is enabled. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        #endregion
    }
}
