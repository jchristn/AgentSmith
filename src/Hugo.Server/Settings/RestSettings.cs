namespace Hugo.Server.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// REST listener settings. Changes require a restart.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// Bind hostname. Default 127.0.0.1 (local development); containers set * or 0.0.0.0 through HUGO_REST_HOSTNAME.
        /// </summary>
        public string Hostname
        {
            get
            {
                return _Hostname;
            }
            set
            {
                _Hostname = String.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value;
            }
        }

        /// <summary>
        /// Port. Minimum 1, maximum 65535, default 8800.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Whether TLS is enabled on the listener. Default false (terminate TLS at the ingress).
        /// </summary>
        public bool Ssl { get; set; } = false;


        /// <summary>
        /// External REST base URL that people and clients outside the cluster use, for example
        /// https://hugo.example.com. Used in A2A agent cards, server info, and links shown to people. Null uses the scheme
        /// (X-Forwarded-Proto when present) and Host header of each request. Environment: HUGO_EXTERNAL_BASE_URL.
        /// Applies live.
        /// </summary>
        public string? ExternalBaseUrl { get; set; } = null;

        /// <summary>
        /// Allowed CORS origins. Empty means any origin (development default).
        /// </summary>
        public List<string> CorsOrigins { get; set; } = new List<string>();


        /// <summary>
        /// Seconds sent in the Retry-After header of 503 DatabaseBusy and DatabaseUnavailable responses, which answer
        /// requests that fail because the database connection pool is exhausted or the database cannot be reached.
        /// Minimum 1, maximum 300, default 2. Applies live.
        /// </summary>
        public int DatabaseRetryAfterSeconds
        {
            get
            {
                return _DatabaseRetryAfterSeconds;
            }
            set
            {
                _DatabaseRetryAfterSeconds = Math.Clamp(value, 1, 300);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "127.0.0.1";
        private int _Port = 8800;
        private int _DatabaseRetryAfterSeconds = 2;

        #endregion
    }
}
