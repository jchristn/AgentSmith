namespace Hugo.Cli.Mcp
{
    using System;

    /// <summary>
    /// Settings of the stdio to Streamable HTTP relay. Not thread-safe; do not change while the relay runs.
    /// </summary>
    public class McpRelaySettings
    {
        #region Public-Members

        /// <summary>
        /// The MCP endpoint, for example <c>http://127.0.0.1:8800/mcp</c> or <c>http://127.0.0.1:8800/mcp/Hugo</c>.
        /// Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        /// <exception cref="ArgumentException">Thrown when not an absolute http or https URL.</exception>
        public Uri Endpoint
        {
            get
            {
                return _Endpoint;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Endpoint));
                if (!value.IsAbsoluteUri || (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps))
                    throw new ArgumentException("The MCP endpoint must be an absolute http or https URL.", nameof(Endpoint));
                _Endpoint = value;
            }
        }

        /// <summary>
        /// Whether to open the standalone GET stream for server-initiated messages after the session is initialized
        /// (session revisions only). A server that answers 405 simply has no such stream. Default true.
        /// </summary>
        public bool ListenForServerMessages { get; set; } = true;

        /// <summary>
        /// After standard input closes, how long to wait for requests still in flight before cancelling them, in
        /// milliseconds. Default 2000. Minimum 0, maximum 60000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
        public int ShutdownGraceMs
        {
            get
            {
                return _ShutdownGraceMs;
            }
            set
            {
                if (value < 0 || value > 60000) throw new ArgumentOutOfRangeException(nameof(ShutdownGraceMs), "Must be 0 to 60000.");
                _ShutdownGraceMs = value;
            }
        }

        /// <summary>
        /// How many times a broken response stream is resumed (GET with <c>Last-Event-ID</c>) before the request is
        /// answered with an error. Default 3. Minimum 0, maximum 20.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
        public int MaxResumeAttempts
        {
            get
            {
                return _MaxResumeAttempts;
            }
            set
            {
                if (value < 0 || value > 20) throw new ArgumentOutOfRangeException(nameof(MaxResumeAttempts), "Must be 0 to 20.");
                _MaxResumeAttempts = value;
            }
        }

        /// <summary>
        /// Delay before reconnecting the standalone GET stream, in milliseconds, unless the server sent a
        /// <c>retry</c> value. Default 1000. Minimum 50, maximum 60000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
        public int ReconnectDelayMs
        {
            get
            {
                return _ReconnectDelayMs;
            }
            set
            {
                if (value < 50 || value > 60000) throw new ArgumentOutOfRangeException(nameof(ReconnectDelayMs), "Must be 50 to 60000.");
                _ReconnectDelayMs = value;
            }
        }

        /// <summary>
        /// Consecutive failures after which the standalone GET stream is given up. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
        public int MaxListenFailures
        {
            get
            {
                return _MaxListenFailures;
            }
            set
            {
                if (value < 1 || value > 1000) throw new ArgumentOutOfRangeException(nameof(MaxListenFailures), "Must be 1 to 1000.");
                _MaxListenFailures = value;
            }
        }

        /// <summary>
        /// Whether to delete the session (<c>DELETE</c> on the endpoint with the session header) when the relay stops.
        /// Default true.
        /// </summary>
        public bool DeleteSessionOnExit { get; set; } = true;

        /// <summary>
        /// Whether to write a diagnostic line to standard error for every message relayed. Default false.
        /// </summary>
        public bool Verbose { get; set; } = false;

        #endregion

        #region Private-Members

        private Uri _Endpoint;
        private int _ShutdownGraceMs = 2000;
        private int _MaxResumeAttempts = 3;
        private int _ReconnectDelayMs = 1000;
        private int _MaxListenFailures = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="endpoint">MCP endpoint.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown when endpoint is not an absolute http or https URL.</exception>
        public McpRelaySettings(Uri endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            _Endpoint = endpoint;
            Endpoint = endpoint;
        }

        #endregion
    }
}
