namespace Hugo.Core.Telemetry
{
    using System;

    /// <summary>
    /// Histogram bucket boundaries used by the Hugo metric catalog. Each property returns a new array. Thread-safe.
    /// </summary>
    public static class TelemetryBuckets
    {
        #region Public-Members

        /// <summary>
        /// Fast operations in seconds (database statements, lock waits): 100 microseconds to 5 seconds.
        /// </summary>
        public static double[] Fast
        {
            get
            {
                return new double[] { 0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0 };
            }
        }

        /// <summary>
        /// Request latency in seconds (REST routes, protocol calls, outbound calls): 1 millisecond to 2 minutes.
        /// </summary>
        public static double[] Request
        {
            get
            {
                return new double[] { 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0, 120.0 };
            }
        }

        /// <summary>
        /// Run stages in seconds (queue, first token, streaming, execution, total): 10 milliseconds to 1 hour.
        /// </summary>
        public static double[] Run
        {
            get
            {
                return new double[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 30.0, 60.0, 120.0, 300.0, 600.0, 1200.0, 1800.0, 3600.0 };
            }
        }

        /// <summary>
        /// Token counts per run: 10 to 5 million.
        /// </summary>
        public static double[] Tokens
        {
            get
            {
                return new double[] { 10, 100, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000, 250000, 500000, 1000000, 5000000 };
            }
        }

        /// <summary>
        /// Payload sizes in bytes: 100 bytes to 64 MiB.
        /// </summary>
        public static double[] Bytes
        {
            get
            {
                return new double[] { 100, 1024, 4096, 16384, 65536, 262144, 1048576, 4194304, 16777216, 67108864 };
            }
        }

        /// <summary>
        /// Cost per run in US dollars: a hundredth of a cent to 50 dollars.
        /// </summary>
        public static double[] Cost
        {
            get
            {
                return new double[] { 0.0001, 0.001, 0.005, 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 50.0 };
            }
        }

        /// <summary>
        /// Small counts (batch sizes, delegation depth, tool calls per run): 0 to 1000.
        /// </summary>
        public static double[] Count
        {
            get
            {
                return new double[] { 0, 1, 2, 3, 5, 10, 20, 50, 100, 250, 500, 1000 };
            }
        }

        /// <summary>
        /// Throughput in tokens per second: 1 to 1000.
        /// </summary>
        public static double[] Rate
        {
            get
            {
                return new double[] { 1, 5, 10, 20, 30, 50, 75, 100, 150, 200, 300, 500, 1000 };
            }
        }

        #endregion
    }
}
