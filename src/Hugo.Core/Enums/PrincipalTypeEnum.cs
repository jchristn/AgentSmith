namespace Hugo.Core.Enums
{
    /// <summary>
    /// Kinds of authenticated principal.
    /// </summary>
    public enum PrincipalTypeEnum
    {
        /// <summary>
        /// System administrator (API key or a user with IsAdmin).
        /// </summary>
        Administrator,

        /// <summary>
        /// Tenant user.
        /// </summary>
        User,

        /// <summary>
        /// Credential (access key).
        /// </summary>
        Credential
    }
}
