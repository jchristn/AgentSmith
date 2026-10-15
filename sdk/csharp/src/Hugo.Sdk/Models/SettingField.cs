namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One server setting with its source, apply mode, and editability.
    /// </summary>
    public class SettingField
    {
        #region Public-Members

        /// <summary>
        /// Dotted settings path, for example Auth.RateLimitPerMinute.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Group.
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Value type.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Current value; masked for secrets.
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Is secret.
        /// </summary>
        public bool IsSecret { get; set; } = false;

        /// <summary>
        /// Is set.
        /// </summary>
        public bool IsSet { get; set; } = false;

        /// <summary>
        /// Where the value comes from (File, Environment, Default).
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Live or Restart.
        /// </summary>
        public string? ApplyMode { get; set; } = null;

        /// <summary>
        /// Editable.
        /// </summary>
        public bool Editable { get; set; } = false;

        /// <summary>
        /// Minimum.
        /// </summary>
        public long? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum.
        /// </summary>
        public long? Maximum { get; set; } = null;

        /// <summary>
        /// Allowed values for enumerated settings.
        /// </summary>
        public List<string>? AllowedValues { get; set; } = null;

        #endregion
    }
}
