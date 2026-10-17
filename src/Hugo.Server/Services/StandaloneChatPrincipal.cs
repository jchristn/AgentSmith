namespace Hugo.Server.Services
{
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;

    internal static class StandaloneChatPrincipal
    {
        internal static RequestContext Create(AgentProfile profile, Credential stored, User owner, bool published)
        {
            Credential credential = Serializer.Copy(stored) ?? throw new System.InvalidOperationException("The publishing credential could not be copied.");
            credential.InheritOwnerBypass = false;
            credential.AgentProfileId = profile.Id;
            credential.LockToProfile = true;
            return new RequestContext
            {
                IsAuthenticated = true, AuthResult = AuthResultEnum.Success, AuthScheme = AuthSchemeEnum.AccessKeySecret,
                PrincipalType = PrincipalTypeEnum.Credential, PrincipalName = "Standalone chat visitor", TenantId = profile.TenantId,
                UserId = owner.Id, CredentialId = credential.Id, User = owner, Credential = credential,
                IsAdmin = false, IsTenantAdmin = false, IsPublicChat = published, PublicChatProfileId = profile.Id,
                PublicChatRevision = profile.ChatRevision, SuppressHistoryBodies = true
            };
        }
    }
}
