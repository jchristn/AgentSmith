namespace Hugo.Server.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Logging settings.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to log to the console. Default true.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether to log to a file. Default false.
        /// </summary>
        public bool FileLogging { get; set; } = false;

        /// <summary>
        /// Log directory. Default logs/.
        /// </summary>
        public string LogDirectory { get; set; } = "logs/";

        /// <summary>
        /// Log file name. Default hugo.log.
        /// </summary>
        public string LogFilename { get; set; } = "hugo.log";

        /// <summary>
        /// Minimum severity (0 debug to 7 emergency). Minimum 0, maximum 7, default 1.
        /// </summary>
        public int MinimumSeverity
        {
            get
            {
                return _MinimumSeverity;
            }
            set
            {
                _MinimumSeverity = Math.Clamp(value, 0, 7);
            }
        }

        /// <summary>
        /// Maximum process-wide known secret variants retained by the scrubber. Default 100000, minimum 100,
        /// maximum 1000000. On overflow logs and shared scrubber outputs fail closed until restart.
        /// </summary>
        public int MaximumKnownSecretValues
        {
            get { return _MaximumKnownSecretValues; }
            set { _MaximumKnownSecretValues = Math.Clamp(value, 100, 1000000); }
        }

        /// <summary>
        /// Maximum retained known secret characters. Default 4194304, minimum 1024, maximum 33554432.
        /// On overflow shared scrubber output fails closed until restart.
        /// </summary>
        public int MaximumKnownSecretCharacters
        {
            get { return _MaximumKnownSecretCharacters; }
            set { _MaximumKnownSecretCharacters = Math.Clamp(value, 1024, 33554432); }
        }

        #endregion

        #region Private-Members

        private int _MinimumSeverity = 1;
        private int _MaximumKnownSecretValues = 100000;
        private int _MaximumKnownSecretCharacters = 4194304;

        #endregion
    }
}
