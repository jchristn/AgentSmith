namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Value kind of a connector setting. Determines storage column, validation, and dashboard control.
    /// </summary>
    public enum SettingValueKindEnum
    {
        /// <summary>
        /// Single-line string.
        /// </summary>
        String = 0,

        /// <summary>
        /// Multi-line text.
        /// </summary>
        Text = 1,

        /// <summary>
        /// 64-bit integer, validated against minimum and maximum.
        /// </summary>
        Integer = 2,

        /// <summary>
        /// Boolean.
        /// </summary>
        Boolean = 3,

        /// <summary>
        /// List of strings (stored one value per line).
        /// </summary>
        StringList = 4,

        /// <summary>
        /// One of a fixed set of string values.
        /// </summary>
        Enum = 5
    }
}
