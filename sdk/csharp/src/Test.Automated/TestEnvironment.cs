namespace Test.Automated
{
    using System;

    /// <summary>
    /// Harness settings, read from environment variables.
    /// </summary>
    public class TestEnvironment
    {
        #region Public-Members

        /// <summary>
        /// Server base URL (HUGO_SDK_SERVER). Default http://127.0.0.1:8800.
        /// </summary>
        public string ServerUrl { get; set; } = "http://127.0.0.1:8800";

        /// <summary>
        /// Administrator email (HUGO_SDK_ADMIN_EMAIL). Default admin@hugo.local.
        /// </summary>
        public string AdminEmail { get; set; } = "admin@hugo.local";

        /// <summary>
        /// Administrator password (HUGO_SDK_ADMIN_PASSWORD). Default password.
        /// </summary>
        public string AdminPassword { get; set; } = "password";

        /// <summary>
        /// Administrator tenant (HUGO_SDK_ADMIN_TENANT). Default default.
        /// </summary>
        public string AdminTenantId { get; set; } = "default";

        /// <summary>
        /// Administrator API key (HUGO_SDK_ADMIN_API_KEY, else HUGO_ADMIN_API_KEY). Null skips the API key test.
        /// </summary>
        public string? AdminApiKey { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Read the environment.
        /// </summary>
        /// <returns>Environment.</returns>
        public static TestEnvironment FromEnvironment()
        {
            TestEnvironment env = new TestEnvironment();
            env.ServerUrl = (Read("HUGO_SDK_SERVER") ?? env.ServerUrl).TrimEnd('/');
            env.AdminEmail = Read("HUGO_SDK_ADMIN_EMAIL") ?? env.AdminEmail;
            env.AdminPassword = Read("HUGO_SDK_ADMIN_PASSWORD") ?? env.AdminPassword;
            env.AdminTenantId = Read("HUGO_SDK_ADMIN_TENANT") ?? env.AdminTenantId;
            env.AdminApiKey = Read("HUGO_SDK_ADMIN_API_KEY") ?? Read("HUGO_ADMIN_API_KEY");
            return env;
        }

        #endregion

        #region Private-Methods

        private static string? Read(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}
