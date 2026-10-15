namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;

    /// <summary>
    /// Data of <c>/api/v1/targets</c>.
    /// </summary>
    public class PromTargetsData
    {
        /// <summary>
        /// Active targets.
        /// </summary>
        public List<PromTarget>? ActiveTargets { get; set; } = null;

        /// <summary>
        /// Dropped targets.
        /// </summary>
        public List<PromTarget>? DroppedTargets { get; set; } = null;
    }
}
