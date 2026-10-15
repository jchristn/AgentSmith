namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Profiles;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Profile resolution shared by every surface (P5.13, D-018): an explicit profile (path, x-hugo-profile header, or
    /// request body; by identifier or name), then the credential's bound profile, then the user's default profile, then
    /// the tenant default. A credential with lock_to_profile may act only as its bound profile. The profile must be
    /// active and visible to the caller. Authorization (Execute on the profile) is the surface's next step. Thread-safe.
    /// </summary>
    public class ProfileResolver
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AgentProfileService _Profiles;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="profiles">Profiles (to provision the tenant default when missing).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ProfileResolver(DatabaseDriverBase database, AgentProfileService profiles)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the profile a request acts as.
        /// </summary>
        /// <param name="ctx">Authenticated caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="explicitProfile">Profile identifier or name given by the request, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolution.</returns>
        /// <exception cref="NotFoundException">Thrown when the named profile does not exist, is inactive, or is not visible.</exception>
        /// <exception cref="ForbiddenException">Thrown with code ProfileLocked when a locked credential asks for another profile.</exception>
        public async Task<ProfileResolution> ResolveAsync(RequestContext ctx, string tenantId, string? explicitProfile, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            Credential? credential = ctx.Credential;
            string? bound = credential?.AgentProfileId;

            if (!String.IsNullOrWhiteSpace(explicitProfile))
            {
                AgentProfile profile = await FindAsync(ctx, tenantId, explicitProfile.Trim(), token).ConfigureAwait(false) ?? throw new NotFoundException("Agent profile '" + explicitProfile + "' not found.");
                if (credential != null && credential.LockToProfile && !String.Equals(bound, profile.Id, StringComparison.Ordinal))
                    throw new ForbiddenException("ProfileLocked", "This credential is locked to its bound agent profile and cannot act as " + profile.Name + ".");
                return Resolution(profile, "Explicit");
            }
            if (!String.IsNullOrEmpty(bound))
            {
                AgentProfile? profile = await UsableAsync(ctx, tenantId, bound, token).ConfigureAwait(false);
                if (profile != null) return Resolution(profile, "Credential");
                if (credential != null && credential.LockToProfile) throw new ForbiddenException("ProfileLocked", "This credential is locked to an agent profile that no longer exists or is inactive.");
            }
            User? user = ctx.User;
            if (user == null && !String.IsNullOrEmpty(ctx.UserId)) user = await _Database.Users.ReadAsync(tenantId, ctx.UserId, token).ConfigureAwait(false);
            if (user != null && !String.IsNullOrEmpty(user.DefaultAgentProfileId))
            {
                AgentProfile? profile = await UsableAsync(ctx, tenantId, user.DefaultAgentProfileId, token).ConfigureAwait(false);
                if (profile != null) return Resolution(profile, "User");
            }
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            if (!String.IsNullOrEmpty(tenant.DefaultAgentProfileId))
            {
                AgentProfile? profile = await UsableAsync(ctx, tenantId, tenant.DefaultAgentProfileId, token).ConfigureAwait(false);
                if (profile != null) return Resolution(profile, "Tenant");
            }
            AgentProfile created = await _Profiles.EnsureDefaultAsync(tenantId, token).ConfigureAwait(false);
            if (!created.Active || !ResourceVisibility.CanSee(created, ctx)) throw new NotFoundException("No agent profile is available to this caller.");
            return Resolution(created, "Tenant");
        }

        #endregion

        #region Private-Methods

        private async Task<AgentProfile?> FindAsync(RequestContext ctx, string tenantId, string idOrName, CancellationToken token)
        {
            AgentProfile? profile = null;
            if (idOrName.StartsWith(Constants.AgentProfileIdPrefix, StringComparison.Ordinal)) profile = await _Database.AgentProfiles.ReadAsync(tenantId, idOrName, token).ConfigureAwait(false);
            if (profile == null) profile = await _Database.AgentProfiles.ReadByNameAsync(tenantId, idOrName, token).ConfigureAwait(false);
            if (profile == null || !profile.Active || !ResourceVisibility.CanSee(profile, ctx)) return null;
            return profile;
        }

        private async Task<AgentProfile?> UsableAsync(RequestContext ctx, string tenantId, string id, CancellationToken token)
        {
            AgentProfile? profile = await _Database.AgentProfiles.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (profile == null || !profile.Active || !ResourceVisibility.CanSee(profile, ctx)) return null;
            return profile;
        }

        private static ProfileResolution Resolution(AgentProfile profile, string source)
        {
            ProfileResolution resolution = new ProfileResolution();
            resolution.Profile = profile;
            resolution.Source = source;
            return resolution;
        }

        #endregion
    }
}
