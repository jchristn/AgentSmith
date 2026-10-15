namespace Hugo.Core.Security
{
    /// <summary>
    /// Where the current master key was loaded from.
    /// </summary>
    public enum MasterKeySourceEnum
    {
        /// <summary>
        /// HUGO_MASTER_KEY (production).
        /// </summary>
        Environment,

        /// <summary>
        /// Secrets.MasterKey in the settings file (development only).
        /// </summary>
        SettingsFile,

        /// <summary>
        /// An existing key file (development only).
        /// </summary>
        KeyFile,

        /// <summary>
        /// A key file generated at this start (development only).
        /// </summary>
        GeneratedKeyFile
    }
}
