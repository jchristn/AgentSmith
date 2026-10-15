namespace Hugo.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// One setting assignment.
    /// </summary>
    public class SettingValue
    {
        /// <summary>
        /// Dotted setting path, for example Rest.Port or Scheduling.LeaseDurationMs.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Value as text.
        /// </summary>
        public string? Value { get; set; } = null;
    }
}
