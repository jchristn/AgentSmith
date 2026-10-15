namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Settings with metadata.
    /// </summary>
    public class SettingsView
    {
        /// <summary>
        /// Settings file path.
        /// </summary>
        public string? SettingsFile { get; set; } = null;

        /// <summary>
        /// Whether file settings can be saved.
        /// </summary>
        public bool SettingsFileWritable { get; set; } = true;

        /// <summary>
        /// Whether a restart-required change was saved since start.
        /// </summary>
        public bool RestartPending { get; set; } = false;

        /// <summary>
        /// Fields.
        /// </summary>
        public List<SettingField> Fields { get; set; } = new List<SettingField>();
    }
}
