namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Authentication settings (plan section 15.2).
    /// </summary>
    public class AuthSettings
    {
        #region Public-Members

        /// <summary>
        /// Token issuer. Default hugo.
        /// </summary>
        public string Issuer { get; set; } = "hugo";

        /// <summary>
        /// Session token signing key. Override with HUGO_AUTH_SIGNING_KEY; never commit a real value. Startup warns while
        /// the built-in default is in use.
        /// </summary>
        public string SigningKey { get; set; } = "hugo-default-signing-key-override-me";

        /// <summary>
        /// Session lifetime for tenant users in minutes. Minimum 1, maximum 10080, default 60.
        /// </summary>
        public int UserSessionMinutes
        {
            get
            {
                return _UserSessionMinutes;
            }
            set
            {
                _UserSessionMinutes = Math.Clamp(value, 1, 10080);
            }
        }

        /// <summary>
        /// Session lifetime for system administrators in minutes. Minimum 1, maximum 10080, default 30.
        /// </summary>
        public int AdminSessionMinutes
        {
            get
            {
                return _AdminSessionMinutes;
            }
            set
            {
                _AdminSessionMinutes = Math.Clamp(value, 1, 10080);
            }
        }

        /// <summary>
        /// Hours after sign-in beyond which a session can no longer be refreshed. Minimum 1, maximum 720, default 12.
        /// </summary>
        public int SessionAbsoluteMaxHours
        {
            get
            {
                return _SessionAbsoluteMaxHours;
            }
            set
            {
                _SessionAbsoluteMaxHours = Math.Clamp(value, 1, 720);
            }
        }

        /// <summary>
        /// System administrator API key accepted in x-api-key on system routes. Override with HUGO_ADMIN_API_KEY. Empty
        /// (the default) disables the scheme.
        /// </summary>
        public string AdminApiKey { get; set; } = String.Empty;

        /// <summary>
        /// Allowed clock skew for signed requests in seconds. Minimum 10, maximum 3600, default 300.
        /// </summary>
        public int SignedRequestSkewSeconds
        {
            get
            {
                return _SignedRequestSkewSeconds;
            }
            set
            {
                _SignedRequestSkewSeconds = Math.Clamp(value, 10, 3600);
            }
        }

        /// <summary>
        /// Token route attempts per source IP and per identity per minute. Minimum 1, maximum 100000, default 10.
        /// </summary>
        public int RateLimitPerMinute
        {
            get
            {
                return _RateLimitPerMinute;
            }
            set
            {
                _RateLimitPerMinute = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// PBKDF2-HMAC-SHA256 iterations for new password hashes (D-009). Older hashes are upgraded at sign-in.
        /// Minimum 600000, maximum 10000000, default 600000.
        /// </summary>
        public int PasswordHashIterations
        {
            get
            {
                return _PasswordHashIterations;
            }
            set
            {
                _PasswordHashIterations = Math.Clamp(value, 600000, 10000000);
            }
        }

        #endregion

        #region Private-Members

        private int _UserSessionMinutes = 60;
        private int _AdminSessionMinutes = 30;
        private int _SessionAbsoluteMaxHours = 12;
        private int _SignedRequestSkewSeconds = 300;
        private int _RateLimitPerMinute = 10;
        private int _PasswordHashIterations = 600000;

        #endregion
    }
}
