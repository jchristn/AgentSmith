namespace Hugo.Core.Enums
{
    /// <summary>
    /// Layer of a prompt template (plan section 10.2).
    /// </summary>
    public enum PromptLayerEnum
    {
        /// <summary>
        /// Shipped in the assembly; immutable.
        /// </summary>
        BuiltIn = 0,

        /// <summary>
        /// Editable default for every tenant (system administrators).
        /// </summary>
        Global = 1,

        /// <summary>
        /// Override for one tenant (tenant administrators).
        /// </summary>
        Tenant = 2
    }
}
