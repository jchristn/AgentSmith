namespace Hugo.Connectors.Logging
{
    using System;

    /// <summary>
    /// Developer-tunable behavior shared by a logging connector and its tools. Set before the connector serves calls;
    /// not thread-safe for mutation.
    /// </summary>
    public class LoggingConnectorOptions
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters kept from each event message; longer messages end with <c>...</c>. Minimum 64, maximum
        /// 65536, default 2000.
        /// </summary>
        public int MaxMessageChars
        {
            get
            {
                return _MaxMessageChars;
            }
            set
            {
                _MaxMessageChars = Math.Clamp(value, 64, 65536);
            }
        }

        /// <summary>
        /// Timeout of the <c>TestConnectionAsync</c> probe. Minimum 1 second, maximum 5 minutes, default 15 seconds.
        /// </summary>
        public TimeSpan TestConnectionTimeout
        {
            get
            {
                return _TestConnectionTimeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromMinutes(5)) value = TimeSpan.FromMinutes(5);
                _TestConnectionTimeout = value;
            }
        }

        /// <summary>
        /// Default number of events returned by <c>logs_search</c> when the caller omits <c>limit</c>. Minimum 1,
        /// maximum 10000, default 100. Always capped by the resource's <c>maxEvents</c>.
        /// </summary>
        public int DefaultSearchLimit
        {
            get
            {
                return _DefaultSearchLimit;
            }
            set
            {
                _DefaultSearchLimit = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Default number of events returned by <c>logs_tail</c> when the caller omits <c>limit</c>. Minimum 1,
        /// maximum 10000, default 50. Always capped by the resource's <c>maxEvents</c>.
        /// </summary>
        public int DefaultTailLimit
        {
            get
            {
                return _DefaultTailLimit;
            }
            set
            {
                _DefaultTailLimit = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Default search window start when the caller omits <c>startTime</c>. Any value accepted by
        /// <see cref="TimeRangeResolver.ParsePoint"/>. Default <c>1h</c>.
        /// </summary>
        public string DefaultWindow
        {
            get
            {
                return _DefaultWindow;
            }
            set
            {
                _DefaultWindow = String.IsNullOrWhiteSpace(value) ? "1h" : value.Trim();
            }
        }

        #endregion

        #region Private-Members

        private int _MaxMessageChars = 2000;
        private TimeSpan _TestConnectionTimeout = TimeSpan.FromSeconds(15);
        private int _DefaultSearchLimit = 100;
        private int _DefaultTailLimit = 50;
        private string _DefaultWindow = "1h";

        #endregion
    }
}
