namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// A resolved, bounded time window. Immutable and thread-safe.
    /// </summary>
    public class ObservabilityTimeWindow
    {
        #region Public-Members

        /// <summary>
        /// Start, UTC.
        /// </summary>
        public DateTimeOffset Start { get; }

        /// <summary>
        /// End, UTC.
        /// </summary>
        public DateTimeOffset End { get; }

        /// <summary>
        /// Length of the window.
        /// </summary>
        public TimeSpan Length
        {
            get
            {
                return End - Start;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        /// <exception cref="ArgumentException">Thrown when start is after end.</exception>
        public ObservabilityTimeWindow(DateTimeOffset start, DateTimeOffset end)
        {
            if (start > end) throw new ArgumentException("Start must not be after end.", nameof(start));
            Start = start.ToUniversalTime();
            End = end.ToUniversalTime();
        }

        #endregion
    }
}
