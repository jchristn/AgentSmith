namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a settings update.
    /// </summary>
    public class SettingsSaveResponse
    {
        #region Public-Members

        /// <summary>
        /// Saved.
        /// </summary>
        public bool Saved { get; set; } = false;

        /// <summary>
        /// Restart required.
        /// </summary>
        public bool RestartRequired { get; set; } = false;

        /// <summary>
        /// Applied live.
        /// </summary>
        public List<string> AppliedLive { get; set; } = new List<string>();

        /// <summary>
        /// Pending restart.
        /// </summary>
        public List<string> PendingRestart { get; set; } = new List<string>();

        /// <summary>
        /// Settings.
        /// </summary>
        public SettingsView? Settings { get; set; } = null;

        #endregion
    }
}
