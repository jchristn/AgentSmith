namespace Hugo.Connectors.Http
{
    using System;

    /// <summary>
    /// Developer-tunable behavior of the <c>http</c> connector and its tools. Set before the connector serves calls;
    /// not thread-safe for mutation.
    /// </summary>
    public class HttpConnectorOptions
    {
        #region Public-Members

        /// <summary>
        /// Timeout of the <c>TestConnectionAsync</c> probe. Minimum 1 second, maximum 5 minutes, default 15 seconds.
        /// The resource's <c>timeoutSeconds</c> applies when it is shorter.
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
        /// Bytes of a binary body shown as base64 in a result. Minimum 0 (none), maximum 4096, default 64.
        /// </summary>
        public int BinaryPreviewBytes
        {
            get
            {
                return _BinaryPreviewBytes;
            }
            set
            {
                _BinaryPreviewBytes = Math.Clamp(value, 0, 4096);
            }
        }

        /// <summary>
        /// Characters kept from each response header value. Minimum 32, maximum 16384, default 1000.
        /// </summary>
        public int MaxHeaderValueChars
        {
            get
            {
                return _MaxHeaderValueChars;
            }
            set
            {
                _MaxHeaderValueChars = Math.Clamp(value, 32, 16384);
            }
        }

        /// <summary>
        /// Token cache used for OAuth2 client credentials. Never null; default <see cref="OAuth2TokenCache.Shared"/>.
        /// </summary>
        public OAuth2TokenCache TokenCache
        {
            get
            {
                return _TokenCache;
            }
            set
            {
                _TokenCache = value ?? OAuth2TokenCache.Shared;
            }
        }

        #endregion

        #region Private-Members

        private TimeSpan _TestConnectionTimeout = TimeSpan.FromSeconds(15);
        private int _BinaryPreviewBytes = 64;
        private int _MaxHeaderValueChars = 1000;
        private OAuth2TokenCache _TokenCache = OAuth2TokenCache.Shared;

        #endregion
    }
}
