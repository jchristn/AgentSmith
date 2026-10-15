namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// One setting change.
    /// </summary>
    public class SettingValueUpdate
    {
        #region Public-Members

        /// <summary>
        /// Dotted settings path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// New value as a string.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
