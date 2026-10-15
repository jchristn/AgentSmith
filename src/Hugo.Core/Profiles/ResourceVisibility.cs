namespace Hugo.Core.Profiles
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// Private versus Tenant visibility of resources and profiles. A Private item is visible to its owner (for a
    /// credential, the credential's owning user), tenant administrators, and system administrators. Thread-safe.
    /// </summary>
    public static class ResourceVisibility
    {
        #region Public-Methods

        /// <summary>
        /// Whether a principal can see a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="principal">Principal, or null for an internal caller (always visible).</param>
        /// <returns>True when visible.</returns>
        public static bool CanSee(Resource resource, RequestContext? principal)
        {
            if (resource == null) return false;
            return CanSee(resource.Visibility, resource.OwnerUserId, principal);
        }

        /// <summary>
        /// Whether a principal can see a profile.
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <param name="principal">Principal, or null for an internal caller.</param>
        /// <returns>True when visible.</returns>
        public static bool CanSee(AgentProfile profile, RequestContext? principal)
        {
            if (profile == null) return false;
            return CanSee(profile.Visibility, profile.OwnerUserId, principal);
        }

        /// <summary>
        /// Whether a principal can see an item with a visibility and owner.
        /// </summary>
        /// <param name="visibility">Visibility.</param>
        /// <param name="ownerUserId">Owner user.</param>
        /// <param name="principal">Principal, or null for an internal caller.</param>
        /// <returns>True when visible.</returns>
        public static bool CanSee(VisibilityEnum visibility, string? ownerUserId, RequestContext? principal)
        {
            if (visibility == VisibilityEnum.Tenant || principal == null) return true;
            if (principal.IsAdmin || principal.IsTenantAdmin || principal.PrincipalType == PrincipalTypeEnum.Administrator) return true;
            return !String.IsNullOrEmpty(ownerUserId) && String.Equals(ownerUserId, principal.UserId, StringComparison.Ordinal);
        }

        #endregion
    }
}
