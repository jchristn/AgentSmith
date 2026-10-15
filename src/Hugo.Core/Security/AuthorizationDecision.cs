namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Result of an authorization evaluation.
    /// </summary>
    public class AuthorizationDecision
    {
        /// <summary>
        /// Outcome.
        /// </summary>
        public AuthorizationResultEnum Result { get; set; } = AuthorizationResultEnum.DeniedImplicit;

        /// <summary>
        /// Whether access is permitted.
        /// </summary>
        public bool Permitted
        {
            get
            {
                return Result == AuthorizationResultEnum.Permitted;
            }
        }

        /// <summary>
        /// Human-readable reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Bypass reason when authorization was skipped by an administrative rule.
        /// </summary>
        public string? BypassReason { get; set; } = null;

        /// <summary>
        /// Grants that matched.
        /// </summary>
        public List<PermissionGrant> MatchedGrants { get; set; } = new List<PermissionGrant>();

        /// <summary>
        /// Permitted decision.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Decision.</returns>
        public static AuthorizationDecision Permit(string reason)
        {
            AuthorizationDecision decision = new AuthorizationDecision();
            decision.Result = AuthorizationResultEnum.Permitted;
            decision.Reason = reason;
            return decision;
        }

        /// <summary>
        /// Denied decision.
        /// </summary>
        /// <param name="result">Denial kind.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>Decision.</returns>
        public static AuthorizationDecision Deny(AuthorizationResultEnum result, string reason)
        {
            AuthorizationDecision decision = new AuthorizationDecision();
            decision.Result = result;
            decision.Reason = reason;
            return decision;
        }
    }
}
