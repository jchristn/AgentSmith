namespace Hugo.Sdk.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Update settings.
    /// </summary>
    public class SettingsUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Changes to apply.
        /// </summary>
        public List<SettingValueUpdate> Values { get; set; } = new List<SettingValueUpdate>();

        #endregion
    }
}
