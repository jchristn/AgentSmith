namespace Hugo.Core.Enums
{
    /// <summary>
    /// Health of a model endpoint as recorded by validation and the health sweep.
    /// </summary>
    public enum ModelHealthStatusEnum
    {
        /// <summary>
        /// Not checked yet.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The last check succeeded.
        /// </summary>
        Healthy = 1,

        /// <summary>
        /// The last check failed.
        /// </summary>
        Unhealthy = 2
    }
}
