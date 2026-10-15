namespace Hugo.Core.Enums
{
    /// <summary>
    /// Who can see and use a resource or agent profile.
    /// </summary>
    public enum VisibilityEnum
    {
        /// <summary>
        /// Only the owner (and tenant and system administrators).
        /// </summary>
        Private = 0,

        /// <summary>
        /// Everyone in the tenant with the matching permission.
        /// </summary>
        Tenant = 1
    }
}
