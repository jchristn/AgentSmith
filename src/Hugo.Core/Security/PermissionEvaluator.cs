namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Core.Enums;

    /// <summary>
    /// Evaluates expanded grants: explicit deny wins, then any permit, otherwise implicit deny. Thread-safe.
    /// </summary>
    public static class PermissionEvaluator
    {
        /// <summary>
        /// Evaluate.
        /// </summary>
        /// <param name="grants">Expanded grants.</param>
        /// <param name="resourceType">Requested resource type.</param>
        /// <param name="operation">Requested operation.</param>
        /// <param name="resourceId">Target resource identifier, or null for tenant-level operations.</param>
        /// <returns>Decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when grants is null.</exception>
        public static AuthorizationDecision Evaluate(IEnumerable<PermissionGrant> grants, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId)
        {
            if (grants == null) throw new ArgumentNullException(nameof(grants));

            List<PermissionGrant> matched = grants.Where(g => Matches(g, resourceType, operation, resourceId)).ToList();
            List<PermissionGrant> denies = matched.Where(g => g.PermissionType == PermissionTypeEnum.Deny).ToList();
            if (denies.Any())
            {
                AuthorizationDecision denied = AuthorizationDecision.Deny(AuthorizationResultEnum.DeniedExplicit, "Explicitly denied by role " + denies[0].Source + ".");
                denied.MatchedGrants = denies;
                return denied;
            }

            List<PermissionGrant> permits = matched.Where(g => g.PermissionType == PermissionTypeEnum.Permit).ToList();
            if (permits.Any())
            {
                AuthorizationDecision permitted = AuthorizationDecision.Permit("Permitted by role " + permits[0].Source + ".");
                permitted.MatchedGrants = permits;
                return permitted;
            }

            return AuthorizationDecision.Deny(AuthorizationResultEnum.DeniedImplicit, "No grant covers " + operation + " on " + resourceType + ".");
        }

        /// <summary>
        /// Whether one grant matches the request.
        /// </summary>
        /// <param name="grant">Grant.</param>
        /// <param name="resourceType">Resource type.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="resourceId">Resource identifier.</param>
        /// <returns>True when it matches.</returns>
        public static bool Matches(PermissionGrant grant, ResourceTypeEnum resourceType, OperationTypeEnum operation, string? resourceId)
        {
            if (grant == null) return false;
            if (!ResourceMatches(grant.ResourceTypes, resourceType)) return false;
            if (!OperationMatches(grant.Operations, operation)) return false;

            if (grant.Scope == ResourceScopeEnum.Resource)
            {
                return !String.IsNullOrEmpty(resourceId) && String.Equals(grant.ResourceId, resourceId, StringComparison.Ordinal);
            }

            if (!String.IsNullOrEmpty(resourceId) && !grant.InheritsToChildren) return false;
            return true;
        }

        private static bool ResourceMatches(List<string> types, ResourceTypeEnum requested)
        {
            foreach (string type in types)
            {
                if (String.Equals(type, "All", StringComparison.OrdinalIgnoreCase)) return true;
                if (String.Equals(type, requested.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool OperationMatches(List<string> operations, OperationTypeEnum requested)
        {
            foreach (string op in operations)
            {
                // Reveal is never implied by All or Write (D-011): only an explicit Reveal grant covers it.
                if (requested == OperationTypeEnum.Reveal)
                {
                    if (String.Equals(op, "Reveal", StringComparison.OrdinalIgnoreCase)) return true;
                    continue;
                }
                if (String.Equals(op, "All", StringComparison.OrdinalIgnoreCase)) return true;
                if (String.Equals(op, requested.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
                if (String.Equals(op, "Write", StringComparison.OrdinalIgnoreCase) &&
                    (requested == OperationTypeEnum.Create || requested == OperationTypeEnum.Update || requested == OperationTypeEnum.Delete || requested == OperationTypeEnum.Write)) return true;
            }
            return false;
        }
    }
}
