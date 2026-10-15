namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Instruction compilation settings (plan section 10.4).
    /// </summary>
    public class InstructionsSettings
    {
        #region Public-Members

        /// <summary>
        /// Characters of a resource's root knowledge entry (for SQL, the database context) included in its section of the
        /// compiled instructions (P5.21). Default 2000, minimum 0 (off), maximum 100000.
        /// </summary>
        public int MaxKnowledgeCharsPerResource
        {
            get
            {
                return _MaxKnowledgeCharsPerResource;
            }
            set
            {
                _MaxKnowledgeCharsPerResource = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Compiled instruction cache entries. Default 1024, minimum 0 (no cache), maximum 100000.
        /// </summary>
        public int CacheEntries
        {
            get
            {
                return _CacheEntries;
            }
            set
            {
                _CacheEntries = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Profiles whose instruction detail is Auto use compact connector instructions when they have more bindings than
        /// this. Default 3, minimum 0 (always compact), maximum 1000.
        /// </summary>
        public int CompactBindingThreshold
        {
            get
            {
                return _CompactBindingThreshold;
            }
            set
            {
                _CompactBindingThreshold = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Characters of third-party guidance (external MCP server instructions, remote agent cards) included in a
        /// binding's section of the compiled instructions (P13.28). Default 4000, minimum 0 (off), maximum 100000.
        /// </summary>
        public int MaxThirdPartyChars
        {
            get
            {
                return _MaxThirdPartyChars;
            }
            set
            {
                _MaxThirdPartyChars = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Longest a compile waits for a first fetch of third-party guidance, in milliseconds. Default 2000, minimum 0
        /// (never wait; the text appears once the background fetch finishes), maximum 60000.
        /// </summary>
        public int ThirdPartyWaitMilliseconds
        {
            get
            {
                return _ThirdPartyWaitMilliseconds;
            }
            set
            {
                _ThirdPartyWaitMilliseconds = Math.Clamp(value, 0, 60000);
            }
        }

        /// <summary>
        /// Seconds fetched third-party guidance is served before it is refreshed in the background. Default 600, minimum 0
        /// (refresh on every compile), maximum 86400.
        /// </summary>
        public int ThirdPartyCacheSeconds
        {
            get
            {
                return _ThirdPartyCacheSeconds;
            }
            set
            {
                _ThirdPartyCacheSeconds = Math.Clamp(value, 0, 86400);
            }
        }

        /// <summary>
        /// Seconds a failed third-party guidance fetch is remembered before it is tried again. Default 60, minimum 0,
        /// maximum 86400.
        /// </summary>
        public int ThirdPartyFailureRetrySeconds
        {
            get
            {
                return _ThirdPartyFailureRetrySeconds;
            }
            set
            {
                _ThirdPartyFailureRetrySeconds = Math.Clamp(value, 0, 86400);
            }
        }

        /// <summary>
        /// Cached third-party guidance entries; the cache is cleared when it grows past this. Default 4096, minimum 1,
        /// maximum 1000000.
        /// </summary>
        public int ThirdPartyCacheEntries
        {
            get
            {
                return _ThirdPartyCacheEntries;
            }
            set
            {
                _ThirdPartyCacheEntries = Math.Clamp(value, 1, 1000000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxKnowledgeCharsPerResource = 2000;
        private int _CacheEntries = 1024;
        private int _CompactBindingThreshold = 3;
        private int _MaxThirdPartyChars = 4000;
        private int _ThirdPartyWaitMilliseconds = 2000;
        private int _ThirdPartyCacheSeconds = 600;
        private int _ThirdPartyFailureRetrySeconds = 60;
        private int _ThirdPartyCacheEntries = 4096;

        #endregion
    }
}
