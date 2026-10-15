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

        #endregion

        #region Private-Members

        private int _MinimumSeverity = 1;

        #endregion
    }
}
