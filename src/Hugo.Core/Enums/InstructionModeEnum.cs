namespace Hugo.Core.Enums
{
    /// <summary>
    /// How a binding changes the connector instructions for one resource.
    /// </summary>
    public enum InstructionModeEnum
    {
        /// <summary>
        /// Use the resolved connector instructions unchanged.
        /// </summary>
        Inherit = 0,

        /// <summary>
        /// Append the binding text after the connector instructions.
        /// </summary>
        Append = 1,

        /// <summary>
        /// Replace the connector instructions with the binding text.
        /// </summary>
        Replace = 2
    }
}
