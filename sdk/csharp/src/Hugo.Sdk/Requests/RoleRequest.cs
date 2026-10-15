namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Create, update, or clone a role.
    /// </summary>
    public class RoleRequest
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Active.
        /// </summary>
        public bool? Active { get; set; } = null;

        #endregion
    }
}
