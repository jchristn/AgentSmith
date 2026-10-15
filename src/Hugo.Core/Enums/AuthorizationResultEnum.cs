namespace Hugo.Core.Enums
{
    /// <summary>
    /// Authorization outcome.
    /// </summary>
    public enum AuthorizationResultEnum
    {
        /// <summary>
        /// Permitted.
        /// </summary>
        Permitted,

        /// <summary>
        /// DeniedExplicit.
        /// </summary>
        DeniedExplicit,

        /// <summary>
        /// DeniedImplicit.
        /// </summary>
        DeniedImplicit,

        /// <summary>
        /// NotFound.
        /// </summary>
        NotFound,

        /// <summary>
        /// Conflict.
        /// </summary>
        Conflict
    }
}
