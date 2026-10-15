namespace Hugo.Core.Enums
{
    /// <summary>
    /// How much connector guidance a profile's compiled instructions carry per resource.
    /// </summary>
    public enum InstructionDetailEnum
    {
        /// <summary>
        /// Full connector instructions when the profile has few bindings, compact ones above
        /// Instructions.CompactBindingThreshold. Default.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Always the full connector instructions.
        /// </summary>
        Full = 1,

        /// <summary>
        /// Always the compact connector instructions; the full text is one call away through hugo_describe_resource.
        /// </summary>
        Compact = 2
    }
}
