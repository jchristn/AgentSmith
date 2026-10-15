namespace Hugo.Core.Inference
{
    using System;

    /// <summary>
    /// Mutable state of one circuit in a <see cref="CircuitBreaker"/>. Guarded by the breaker; not thread-safe on
    /// its own.
    /// </summary>
    public class CircuitBreakerState
    {
        /// <summary>
        /// Consecutive retryable failures since the last success.
        /// </summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>
        /// When the circuit stays open until (UTC). <see cref="DateTime.MinValue"/> when closed.
        /// </summary>
        public DateTime OpenUntilUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Number of times the circuit has opened.
        /// </summary>
        public long TimesOpened { get; set; } = 0;
    }
}
