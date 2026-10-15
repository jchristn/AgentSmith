namespace Hugo.Sdk.Enums
{
    /// <summary>
    /// Layer of a layered prompt template.
    /// </summary>
    public enum PromptLayerEnum
    {
        /// <summary>
        /// The default shipped with Hugo.
        /// </summary>
        BuiltIn,

        /// <summary>
        /// The server-wide override (system administrators).
        /// </summary>
        Global,

        /// <summary>
        /// The tenant override.
        /// </summary>
        Tenant
    }
}
