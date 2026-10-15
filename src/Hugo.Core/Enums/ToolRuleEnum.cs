namespace Hugo.Core.Enums
{
    /// <summary>
    /// Per-binding rule for one tool.
    /// </summary>
    public enum ToolRuleEnum
    {
        /// <summary>
        /// Allow the tool within the access mode. Cannot override a ReadOnly denial.
        /// </summary>
        Allow = 0,

        /// <summary>
        /// Never allow the tool on this binding.
        /// </summary>
        Deny = 1,

        /// <summary>
        /// Require approval for every call, even Read calls.
        /// </summary>
        RequireApproval = 2
    }
}
