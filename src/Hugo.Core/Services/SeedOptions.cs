namespace Hugo.Core.Services
{
    using System;

    /// <summary>
    /// First-boot seed values (plan section 17.3). Seeded accounts must change their password at first sign-in.
    /// </summary>
    public class SeedOptions
    {
        #region Public-Members

        /// <summary>
        /// System administrator email. Default admin@hugo.local.
        /// </summary>
        public string AdminEmail { get; set; } = "admin@hugo.local";

        /// <summary>
        /// System administrator initial password. Default password (must be changed at first sign-in).
        /// </summary>
        public string AdminPassword { get; set; } = "password";

        /// <summary>
        /// Tenant administrator email. Default anderson@hugo.local.
        /// </summary>
        public string TenantAdminEmail { get; set; } = "anderson@hugo.local";

        /// <summary>
        /// Tenant administrator first name. Default Thomas.
        /// </summary>
        public string TenantAdminFirstName { get; set; } = "Thomas";

        /// <summary>
        /// Tenant administrator last name. Default Anderson.
        /// </summary>
        public string TenantAdminLastName { get; set; } = "Anderson";

        /// <summary>
        /// Tenant administrator initial password. Default password (must be changed at first sign-in).
        /// </summary>
        public string TenantAdminPassword { get; set; } = "password";

        /// <summary>
        /// PBKDF2 iterations for seeded password hashes. Minimum 10000, maximum 10000000, default 600000.
        /// </summary>
        public int PasswordHashIterations
        {
            get
            {
                return _PasswordHashIterations;
            }
            set
            {
                _PasswordHashIterations = Math.Clamp(value, 10000, 10000000);
            }
        }

        #endregion

        #region Private-Members

        private int _PasswordHashIterations = 600000;

        #endregion
    }
}
