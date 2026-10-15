namespace Test.Shared.Connectors.Echo
{
    /// <summary>
    /// Behavior of an echo tool.
    /// </summary>
    public enum EchoToolKindEnum
    {
        /// <summary>
        /// Echo text (Read, Write, or Destructive by base effect).
        /// </summary>
        Echo,

        /// <summary>
        /// Echo text; the command argument decides the effect per call.
        /// </summary>
        Classify,

        /// <summary>
        /// Write a resource knowledge entry.
        /// </summary>
        Knowledge,

        /// <summary>
        /// Call the profile's model.
        /// </summary>
        Model,

        /// <summary>
        /// Learn a resource setting (a pinned host key).
        /// </summary>
        Learn
    }
}
