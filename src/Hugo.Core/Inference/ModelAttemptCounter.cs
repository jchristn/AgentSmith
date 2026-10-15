namespace Hugo.Core.Inference
{
    /// <summary>
    /// Counts attempts across retries and endpoints for one gateway call. Used by one call at a time; not
    /// thread-safe.
    /// </summary>
    public class ModelAttemptCounter
    {
        /// <summary>
        /// Attempts made.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Whether the fallback endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;
    }
}
