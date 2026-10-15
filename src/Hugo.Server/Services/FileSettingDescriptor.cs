namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Server.Settings;

    /// <summary>
    /// Describes one settings-file field for the settings API: how to read it, parse and apply it, and whether a
    /// change applies live or requires a restart.
    /// </summary>
    public class FileSettingDescriptor
    {
        /// <summary>
        /// Dotted path, for example Rest.Port.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Value type: string, int, bool, enum, or secret.
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Whether the value is secret.
        /// </summary>
        public bool IsSecret { get; set; } = false;

        /// <summary>
        /// Whether changes apply without a restart.
        /// </summary>
        public bool Live { get; set; } = false;

        /// <summary>
        /// Minimum for integers.
        /// </summary>
        public long? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum for integers.
        /// </summary>
        public long? Maximum { get; set; } = null;

        /// <summary>
        /// Allowed values for enums.
        /// </summary>
        public List<string>? AllowedValues { get; set; } = null;

        /// <summary>
        /// Reads the current value as text.
        /// </summary>
        public Func<HugoSettings, string?> Get { get; set; } = s => null;

        /// <summary>
        /// Applies a validated text value.
        /// </summary>
        public Action<HugoSettings, string?> Set { get; set; } = (s, v) => { };
    }
}
