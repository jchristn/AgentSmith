namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Location and credentials of the Redis testbed (<c>docker/testbed/redis/compose.yaml</c>). The port can be overridden
    /// with <c>HUGO_TESTBED_REDIS_PORT</c>.
    /// </summary>
    public static class RedisTestbed
    {
        #region Public-Members

        /// <summary>
        /// Whether testbed suites run (<c>HUGO_TESTBED=1</c>).
        /// </summary>
        public static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("HUGO_TESTBED") == "1";
            }
        }

        /// <summary>
        /// Skip reason when the testbed is disabled.
        /// </summary>
        public static string SkipReason { get; } = "Redis testbed not enabled. Start docker/testbed/redis/compose.yaml and set HUGO_TESTBED=1.";

        /// <summary>
        /// Host.
        /// </summary>
        public static string Host { get; } = "127.0.0.1";

        /// <summary>
        /// Port.
        /// </summary>
        public static int Port
        {
            get
            {
                string? v = Environment.GetEnvironmentVariable("HUGO_TESTBED_REDIS_PORT");
                return v != null && Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 28379;
            }
        }

        /// <summary>
        /// Password of the default user.
        /// </summary>
        public static string Password { get; } = "hugo-testbed-redis";

        /// <summary>
        /// Read-only ACL user.
        /// </summary>
        public static string ReaderUser { get; } = "hugo-reader";

        /// <summary>
        /// Password of <see cref="ReaderUser"/>.
        /// </summary>
        public static string ReaderPassword { get; } = "hugo-reader-password";

        /// <summary>
        /// Keys seeded by <c>docker/testbed/redis/seed.sh</c>.
        /// </summary>
        public static int SeededKeys { get; } = 512;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Settings for the testbed.
        /// </summary>
        /// <param name="extra">Settings to add or override, or null.</param>
        /// <returns>Settings.</returns>
        public static Dictionary<string, string> Settings(Dictionary<string, string>? extra = null)
        {
            Dictionary<string, string> s = new Dictionary<string, string>
            {
                { "endpoints", Host + ":" + Port.ToString(CultureInfo.InvariantCulture) },
                { "connectTimeoutSeconds", "5" }
            };
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) s[kv.Key] = kv.Value;
            }
            return s;
        }

        #endregion
    }
}
