namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A setting a connector declares.
    /// </summary>
    public class ConnectorSetting
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Help.
        /// </summary>
        public string? Help { get; set; } = null;

        /// <summary>
        /// String, Text, Integer, Boolean, StringList, or Enum.
        /// </summary>
        public string? Kind { get; set; } = null;

        /// <summary>
        /// Required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Default value.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        /// <summary>
        /// Minimum.
        /// </summary>
        public long? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum.
        /// </summary>
        public long? Maximum { get; set; } = null;

        /// <summary>
        /// Allowed values.
        /// </summary>
        public List<string> AllowedValues { get; set; } = new List<string>();

        /// <summary>
        /// Pattern.
        /// </summary>
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// Group.
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// Show in summary.
        /// </summary>
        public bool ShowInSummary { get; set; } = false;

        #endregion
    }
}
