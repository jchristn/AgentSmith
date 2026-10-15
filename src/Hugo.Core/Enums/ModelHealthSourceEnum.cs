namespace Hugo.Core.Enums
{
    /// <summary>
    /// What recorded a model endpoint health check.
    /// </summary>
    public enum ModelHealthSourceEnum
    {
        /// <summary>
        /// The periodic background health sweep.
        /// </summary>
        Sweep = 0,

        /// <summary>
        /// An operator's validate request (POST /v1.0/api/model-endpoints/{id}/validate).
        /// </summary>
        Validate = 1
    }
}
