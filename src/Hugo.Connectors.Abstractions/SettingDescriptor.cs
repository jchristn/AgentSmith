namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Describes one connector setting. The server validates resource settings against these descriptors and the
    /// dashboard renders resource forms from them. Not thread-safe for mutation; treat as immutable after construction.
    /// </summary>
    public class SettingDescriptor
    {
        #region Public-Members

        /// <summary>
        /// Setting key, camelCase, unique within the connector (for example <c>host</c>). Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Key));
                _Key = value;
            }
        }

        /// <summary>
        /// English label shown when no translation exists. Defaults to the key.
        /// </summary>
        public string Label
        {
            get
            {
                return String.IsNullOrEmpty(_Label) ? _Key : _Label;
            }
            set
            {
                _Label = value ?? String.Empty;
            }
        }

        /// <summary>
        /// English help text. May be empty.
        /// </summary>
        public string Help { get; set; } = String.Empty;

        /// <summary>
        /// Value kind. Default <see cref="SettingValueKindEnum.String"/>.
        /// </summary>
        public SettingValueKindEnum Kind { get; set; } = SettingValueKindEnum.String;

        /// <summary>
        /// Whether a value is required. Default false.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Default value as a string (integers in invariant culture, booleans as <c>true</c>/<c>false</c>, string lists
        /// newline separated). Null means no default.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        /// <summary>
        /// Minimum value for <see cref="SettingValueKindEnum.Integer"/>. Null means unbounded.
        /// </summary>
        public long? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum value for <see cref="SettingValueKindEnum.Integer"/>. Null means unbounded.
        /// </summary>
        public long? Maximum { get; set; } = null;

        /// <summary>
        /// Allowed values for <see cref="SettingValueKindEnum.Enum"/>. Never null.
        /// </summary>
        public List<string> AllowedValues
        {
            get
            {
                return _AllowedValues;
            }
            set
            {
                _AllowedValues = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Optional regular expression a string value must match. Null means no pattern.
        /// </summary>
        public string? Pattern { get; set; } = null;

        /// <summary>
        /// Form group name used by the dashboard (for example <c>Connection</c>, <c>Limits</c>, <c>Safety</c>). Default <c>General</c>.
        /// </summary>
        public string Group { get; set; } = "General";

        /// <summary>
        /// Whether this value is shown in resource summaries. Default false.
        /// </summary>
        public bool ShowInSummary { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Key = "key";
        private string _Label = String.Empty;
        private List<string> _AllowedValues = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty descriptor.
        /// </summary>
        public SettingDescriptor()
        {
        }

        /// <summary>
        /// Instantiate with key, kind, and label.
        /// </summary>
        /// <param name="key">Setting key.</param>
        /// <param name="kind">Value kind.</param>
        /// <param name="label">English label.</param>
        /// <exception cref="ArgumentNullException">Thrown when key is null or whitespace.</exception>
        public SettingDescriptor(string key, SettingValueKindEnum kind, string label)
        {
            Key = key;
            Kind = kind;
            Label = label;
        }

        #endregion
    }
}
