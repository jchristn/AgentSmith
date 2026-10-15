namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Result of a settings save.
    /// </summary>
    public class SettingsSaveResponse
    {
        /// <summary>
        /// Whether the change was saved.
        /// </summary>
        public bool Saved { get; set; } = true;

        /// <summary>
        /// Whether a restart is needed for some changes.
        /// </summary>
        public bool RestartRequired { get; set; } = false;

        /// <summary>
        /// Paths applied immediately.
        /// </summary>
        public List<string> AppliedLive { get; set; } = new List<string>();

        /// <summary>
        /// Paths that apply after restart.
        /// </summary>
        public List<string> PendingRestart { get; set; } = new List<string>();

        /// <summary>
        /// Updated settings.
        /// </summary>
        public SettingsView Settings { get; set; } = new SettingsView();
    }
}
