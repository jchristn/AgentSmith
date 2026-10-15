namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Server settings with metadata.
    /// </summary>
    public class SettingsView
    {
        #region Public-Members

        /// <summary>
        /// Settings file.
        /// </summary>
        public string? SettingsFile { get; set; } = null;

        /// <summary>
        /// Settings file writable.
        /// </summary>
        public bool SettingsFileWritable { get; set; } = false;

        /// <summary>
        /// Restart pending.
        /// </summary>
        public bool RestartPending { get; set; } = false;

        /// <summary>
        /// Fields.
        /// </summary>
        public List<SettingField> Fields { get; set; } = new List<SettingField>();

        #endregion
    }
}
