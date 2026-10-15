namespace Hugo.Connectors.Observability
{
    using System;
    using System.Numerics;

    /// <summary>
    /// One log line flattened from a stream, for ordering and rendering.
    /// </summary>
    public class LokiLine
    {
        /// <summary>
        /// Short stream key (s1, s2, ...).
        /// </summary>
        public string StreamKey { get; set; } = String.Empty;

        /// <summary>
        /// Index of the stream in the result.
        /// </summary>
        public int StreamIndex { get; set; } = 0;

        /// <summary>
        /// Timestamp in Unix nanoseconds.
        /// </summary>
        public BigInteger Nanoseconds { get; set; } = BigInteger.Zero;

        /// <summary>
        /// Line text.
        /// </summary>
        public string Line { get; set; } = String.Empty;
    }
}
