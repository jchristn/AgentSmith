namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// One setting with metadata for the Settings page.
    /// </summary>
    public class SettingField
    {
        /// <summary>
        /// Dotted path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Group, for example Rest or Scheduling.
        /// </summary>
        public string Group { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Value type: string, int, bool, enum, secret.
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Current value; for secrets, null (see IsSet).
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Whether the value is a secret.
        /// </summary>
        public bool IsSecret { get; set; } = false;

        /// <summary>
        /// For secrets, whether a value is configured.
        /// </summary>
        public bool IsSet { get; set; } = false;

        /// <summary>
        /// Source: File, Environment, ConfigMap, or Database.
        /// </summary>
        public string Source { get; set; } = "File";

        /// <summary>
        /// Live or RestartRequired.
        /// </summary>
        public string ApplyMode { get; set; } = "RestartRequired";

        /// <summary>
        /// Whether the dashboard may change it.
        /// </summary>
        public bool Editable { get; set; } = true;

        /// <summary>
        /// Minimum for numeric values.
        /// </summary>
        public long? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum for numeric values.
        /// </summary>
        public long? Maximum { get; set; } = null;

        /// <summary>
        /// Allowed values for enums.
        /// </summary>
        public List<string>? AllowedValues { get; set; } = null;
    }
}
