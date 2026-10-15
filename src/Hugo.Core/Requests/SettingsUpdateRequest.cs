namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Settings update. Each entry sets one setting by dotted path; secrets are changed only when a new non-mask value is supplied.
    /// </summary>
    public class SettingsUpdateRequest
    {
        /// <summary>
        /// Values to set.
        /// </summary>
        public List<SettingValue> Values { get; set; } = new List<SettingValue>();
    }
}
