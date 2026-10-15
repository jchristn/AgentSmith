namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Authenticated request context. Created by the authentication hook and stored in the HTTP context metadata.
    /// Not thread-safe; one instance per request.
    /// </summary>
    public class RequestContext
    {
        #region Public-Members

        /// <summary>
        /// Request identifier.
        /// </summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Whether the request is authenticated.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// Authentication result.
        /// </summary>
        public AuthResultEnum AuthResult { get; set; } = AuthResultEnum.NotFound;

        /// <summary>
        /// Authentication scheme used.
        /// </summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>
        /// Principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Principal display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Tenant identifier bound to the principal. Null for system administrators authenticated by API key.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User identifier, for user principals and for the owner of a credential.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier, for credential principals.
        /// </summary>
        public string? CredentialId { get; set; } = null;




        /// <summary>
        /// Session identifier when a session token was used.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Whether the principal is a system administrator.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the principal is a tenant administrator.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Authenticated user, when applicable.
        /// </summary>
        public User? User { get; set; } = null;

        /// <summary>
        /// Authenticated credential, when applicable.
        /// </summary>
        public Credential? Credential { get; set; } = null;

        /// <summary>
        /// Source IP.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Requested resource type, set by authorization.
        /// </summary>
        public ResourceTypeEnum? ResourceType { get; set; } = null;

        /// <summary>
        /// Requested operation, set by authorization.
        /// </summary>
        public OperationTypeEnum? Operation { get; set; } = null;

        /// <summary>
        /// Authorization result, set by authorization.
        /// </summary>
        public AuthorizationResultEnum? AuthorizationResult { get; set; } = null;

        /// <summary>
        /// Received (UTC).
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When true, request history records this request without its request and response bodies (set by routes whose
        /// bodies carry plaintext secrets, such as secret reveal). Default false.
        /// </summary>
        public bool SuppressHistoryBodies { get; set; } = false;

        /// <summary>
        /// The authenticated session, when the request presented a session token; null otherwise.
        /// </summary>
        public AuthSession? Session { get; set; } = null;

        /// <summary>
        /// Principal identifier: user, credential, or run identifier.
        /// </summary>
        public string? PrincipalId
        {
            get
            {
                switch (PrincipalType)
                {
                    case PrincipalTypeEnum.Credential:
                        return CredentialId;
                    default:
                        return UserId;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Unauthenticated context.
        /// </summary>
        /// <returns>Context.</returns>
        public static RequestContext Unauthenticated()
        {
            return new RequestContext();
        }

        #endregion
    }
}
