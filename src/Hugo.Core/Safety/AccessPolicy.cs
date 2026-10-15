namespace Hugo.Core.Safety
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;

    /// <summary>
    /// The access mode matrix (plan section 11.2). A pure function: the same input always yields the same decision.
    /// Thread-safe.
    /// </summary>
    public static class AccessPolicy
    {
        /// <summary>
        /// Decide whether a classified call may run.
        /// Order: classifier rejection, tenant lockdown, tool Deny rule, resource ceiling, access mode matrix,
        /// then RequireApproval rule and classifier AlwaysRequireApproval upgrade Allowed to ApprovalRequired.
        /// </summary>
        /// <param name="input">Policy input.</param>
        /// <returns>Decision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when input is null.</exception>
        public static AccessPolicyDecision Decide(AccessPolicyInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            EffectClassification c = input.Classification ?? new EffectClassification();
            AccessModeEnum mode = input.AccessMode;
            if (input.ResourceCeiling.HasValue && input.ResourceCeiling.Value < mode) mode = input.ResourceCeiling.Value;

            if (c.Rejected)
                return new AccessPolicyDecision(InvocationDecisionEnum.Denied, mode, "classifier_rejected", "The call was rejected by the tool's safety rules: " + c.Reason + ".");

            if (input.TenantLockdown && c.Effect != EffectEnum.Read)
                return new AccessPolicyDecision(InvocationDecisionEnum.Denied, mode, "tenant_lockdown", "The tenant is in lockdown; only Read calls are allowed.");

            if (input.ToolRule == ToolRuleEnum.Deny)
                return new AccessPolicyDecision(InvocationDecisionEnum.Denied, mode, "tool_denied", "This tool is denied on this resource for this profile.");

            InvocationDecisionEnum decision;
            string code;
            string reason;
            switch (c.Effect)
            {
                case EffectEnum.Read:
                    decision = InvocationDecisionEnum.Allowed;
                    code = "allowed";
                    reason = "Read call.";
                    break;
                case EffectEnum.Write:
                    if (mode == AccessModeEnum.ReadOnly)
                    {
                        decision = InvocationDecisionEnum.Denied;
                        code = "readonly_binding";
                        reason = "The resource is ReadOnly for this profile; Write calls are not allowed (" + c.Reason + ").";
                    }
                    else if (mode == AccessModeEnum.ReadWriteWithApproval)
                    {
                        decision = InvocationDecisionEnum.ApprovalRequired;
                        code = "approval_required";
                        reason = "Write calls on this resource require approval (" + c.Reason + ").";
                    }
                    else
                    {
                        decision = InvocationDecisionEnum.Allowed;
                        code = "allowed";
                        reason = "Write call allowed by ReadWrite access.";
                    }
                    break;
                default:
                    if (mode == AccessModeEnum.ReadOnly)
                    {
                        decision = InvocationDecisionEnum.Denied;
                        code = "readonly_binding";
                        reason = "The resource is ReadOnly for this profile; Destructive calls are not allowed (" + c.Reason + ").";
                    }
                    else if (mode == AccessModeEnum.ReadWriteWithApproval || input.RequireApprovalForDestructive)
                    {
                        decision = InvocationDecisionEnum.ApprovalRequired;
                        code = "approval_required";
                        reason = "Destructive calls on this resource require approval (" + c.Reason + ").";
                    }
                    else
                    {
                        decision = InvocationDecisionEnum.Allowed;
                        code = "allowed";
                        reason = "Destructive call allowed: the binding opted out of destructive approvals.";
                    }
                    break;
            }

            if (decision == InvocationDecisionEnum.Allowed && input.ToolRule == ToolRuleEnum.RequireApproval)
                return new AccessPolicyDecision(InvocationDecisionEnum.ApprovalRequired, mode, "tool_requires_approval", "This tool requires approval on this resource for this profile.");

            if (decision == InvocationDecisionEnum.Allowed && c.AlwaysRequireApproval && c.Effect != EffectEnum.Read)
                return new AccessPolicyDecision(InvocationDecisionEnum.ApprovalRequired, mode, "always_requires_approval", "This action always requires approval (" + c.Reason + ").");

            return new AccessPolicyDecision(decision, mode, code, reason);
        }
    }
}
