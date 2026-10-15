namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// Definition of a cluster runtime setting stored in the database.
    /// </summary>
    public class ClusterSettingDefinition
    {
        /// <summary>
        /// Dotted key, for example Scheduling.LeaseDurationMs.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Value type: int, bool, or string.
        /// </summary>
        public string Type { get; set; } = "int";

        /// <summary>
        /// Default value as text.
        /// </summary>
        public string DefaultValue { get; set; } = String.Empty;

        /// <summary>
        /// Minimum for integers.
        /// </summary>
        public long Minimum { get; set; } = 0;

        /// <summary>
        /// Maximum for integers.
        /// </summary>
        public long Maximum { get; set; } = Int32.MaxValue;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// For strings, whether the value must be empty or an absolute http or https URL. Default false.
        /// </summary>
        public bool IsUrl { get; set; } = false;

        /// <summary>
        /// Environment variable that overrides the stored value when set to a non-empty value, or null when the
        /// setting has none. An overridden setting reports source Environment and cannot be changed through the API.
        /// </summary>
        public string? EnvironmentVariable { get; set; } = null;
    }
}
