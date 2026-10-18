namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>Typed BenchmarkMeasurement contract for the public API benchmark.</summary>
    public class BenchmarkMeasurement
    {
        /// <summary>Case value.</summary>
        public string Case { get; set; } = String.Empty;
        /// <summary>Operation value.</summary>
        public string Operation { get; set; } = String.Empty;
        /// <summary>Cache value.</summary>
        public string Cache { get; set; } = String.Empty;
        /// <summary>AttachmentCount value.</summary>
        public int AttachmentCount { get; set; } = 0;
        /// <summary>DocumentBytes value.</summary>
        public int DocumentBytes { get; set; } = 0;
        /// <summary>PromptBytes value.</summary>
        public int PromptBytes { get; set; } = 0;
        /// <summary>EstimatedTokens value.</summary>
        public long EstimatedTokens { get; set; } = 0;
        /// <summary>Successes value.</summary>
        public int Successes { get; set; } = 0;
        /// <summary>P50Ms value.</summary>
        public double P50Ms { get; set; } = 0;
        /// <summary>P95Ms value.</summary>
        public double P95Ms { get; set; } = 0;
        /// <summary>MeanMs value.</summary>
        public double MeanMs { get; set; } = 0;
        /// <summary>MaxMs value.</summary>
        public double MaxMs { get; set; } = 0;
        /// <summary>LatencyMs value.</summary>
        public List<double> LatencyMs { get; set; } = new List<double>();
    }
}
