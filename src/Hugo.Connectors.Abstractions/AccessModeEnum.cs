namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Access mode of a binding between an agent profile and a resource.
    /// </summary>
    public enum AccessModeEnum
    {
        /// <summary>
        /// Read calls are allowed; Write and Destructive calls are denied.
        /// </summary>
        ReadOnly = 0,

        /// <summary>
        /// Read calls are allowed; Write and Destructive calls require human approval.
        /// </summary>
        ReadWriteWithApproval = 1,

        /// <summary>
        /// Read and Write calls are allowed; Destructive calls require approval unless the binding opts out.
        /// </summary>
        ReadWrite = 2
    }
}
