namespace Hugo.Core.Enums
{
    /// <summary>
    /// Operation types used for authorization. Write expands to Create, Update, and Delete. Reveal is never implied by Write or All; only an explicit Reveal grant (held by TenantAdmin) or a bypass permits it.
    /// </summary>
    public enum OperationTypeEnum
    {
        /// <summary>
        /// Every operation except Reveal.
        /// </summary>
        All,

        /// <summary>
        /// Create.
        /// </summary>
        Create,

        /// <summary>
        /// Read.
        /// </summary>
        Read,

        /// <summary>
        /// Create, Update, and Delete.
        /// </summary>
        Write,

        /// <summary>
        /// Update.
        /// </summary>
        Update,

        /// <summary>
        /// Delete.
        /// </summary>
        Delete,

        /// <summary>
        /// Execute (invoke an agent profile or run an action).
        /// </summary>
        Execute,

        /// <summary>
        /// Administrative operations.
        /// </summary>
        Admin,

        /// <summary>
        /// Reveal plaintext secret values (Secret only).
        /// </summary>
        Reveal
    }
}
